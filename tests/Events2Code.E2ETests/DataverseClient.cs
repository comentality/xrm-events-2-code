using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Events2Code.E2ETests
{
    /// <summary>
    /// Minimal Dataverse Web API client authenticating with the e2e service principal
    /// from tests/e2e-fixtures/.env. Deliberately SDK-free so the harness only needs the .env.
    /// </summary>
    public sealed class DataverseClient : IDisposable
    {
        private readonly HttpClient _http;

        public string OrgUrl { get; }
        public string RepoRoot { get; }

        private DataverseClient(HttpClient http, string orgUrl, string repoRoot)
        {
            _http = http;
            OrgUrl = orgUrl;
            RepoRoot = repoRoot;
        }

        public static DataverseClient Connect()
        {
            var repoRoot = FindRepoRoot();
            var envPath = Path.Combine(repoRoot, "tests", "e2e-fixtures", ".env");
            if (!File.Exists(envPath))
                NUnit.Framework.Assert.Ignore("tests/e2e-fixtures/.env not found - e2e suite skipped. See tests/e2e-fixtures/README.md.");

            var env = File.ReadAllLines(envPath)
                .Where(l => l.Contains("=") && !l.TrimStart().StartsWith("#"))
                .Select(l => l.Split(new[] { '=' }, 2))
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

            var orgUrl = env["DATAVERSE_URL"].TrimEnd('/');
            var token = GetToken(env["TENANT_ID"], env["CLIENT_ID"], env["CLIENT_SECRET"], orgUrl);

            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            http.DefaultRequestHeaders.Add("OData-Version", "4.0");
            return new DataverseClient(http, orgUrl, repoRoot);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Events2Code", "Events2Code.csproj")))
                dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("Repo root not found above " + AppContext.BaseDirectory);
            return dir.FullName;
        }

        private static string GetToken(string tenantId, string clientId, string clientSecret, string orgUrl)
        {
            using (var http = new HttpClient())
            {
                var body = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = orgUrl + "/.default"
                });
                var resp = http.PostAsync("https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/token", body)
                    .GetAwaiter().GetResult();
                var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException("Token request failed (" + (int)resp.StatusCode + "). Check tests/e2e-fixtures/.env.");
                return (string)JObject.Parse(json)["access_token"];
            }
        }

        private string Api(string path) => OrgUrl + "/api/data/v9.2/" + path;

        private async Task<string> SendAsync(HttpMethod method, string path, string jsonBody = null)
        {
            using (var req = new HttpRequestMessage(method, Api(path)))
            {
                if (jsonBody != null)
                    req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                using (var resp = await _http.SendAsync(req))
                {
                    var content = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new DataverseApiException(method + " " + path + " -> " + (int)resp.StatusCode + ": " + content);
                    return content;
                }
            }
        }

        public async Task<string> GetFormXmlAsync(Guid formId)
        {
            var json = await SendAsync(HttpMethod.Get, "systemforms(" + formId + ")?$select=formxml");
            return (string)JObject.Parse(json)["formxml"];
        }

        public Task SetFormXmlAsync(Guid formId, string formXml)
        {
            var body = new JObject { ["formxml"] = formXml }.ToString();
            return SendAsync(new HttpMethod("PATCH"), "systemforms(" + formId + ")", body);
        }

        public Task PublishAsync(string parameterXml)
        {
            var body = new JObject { ["ParameterXml"] = parameterXml }.ToString();
            return SendAsync(HttpMethod.Post, "PublishXml", body);
        }

        public Task PublishEntityAsync(string entityLogicalName)
        {
            // Same ParameterXml the tool itself uses (Events2CodeControl.BtnUnregister_Click)
            return PublishAsync("<importexportxml><entities><entity>" + entityLogicalName + "</entity></entities></importexportxml>");
        }

        public async Task<Guid?> FindWebResourceIdAsync(string name)
        {
            var json = await SendAsync(HttpMethod.Get,
                "webresourceset?$select=webresourceid&$filter=name eq '" + name + "'");
            var rows = (JArray)JObject.Parse(json)["value"];
            if (rows.Count == 0) return null;
            return Guid.Parse((string)rows[0]["webresourceid"]);
        }

        /// <summary>Creates or updates a JScript web resource and publishes it. Returns its id.</summary>
        public async Task<Guid> UpsertWebResourceAsync(string name, string jsContent)
        {
            var contentB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsContent));
            var existing = await FindWebResourceIdAsync(name);
            Guid id;
            if (existing.HasValue)
            {
                id = existing.Value;
                var body = new JObject { ["content"] = contentB64 }.ToString();
                await SendAsync(new HttpMethod("PATCH"), "webresourceset(" + id + ")", body);
            }
            else
            {
                id = Guid.NewGuid();
                var body = new JObject
                {
                    ["webresourceid"] = id.ToString(),
                    ["name"] = name,
                    ["displayname"] = name,
                    ["webresourcetype"] = 3,
                    ["content"] = contentB64
                }.ToString();
                await SendAsync(HttpMethod.Post, "webresourceset", body);
            }
            await PublishAsync("<importexportxml><webresources><webresource>{" + id + "}</webresource></webresources></importexportxml>");
            return id;
        }

        public async Task DeleteWebResourceIfExistsAsync(string name)
        {
            var id = await FindWebResourceIdAsync(name);
            if (id.HasValue)
                await SendAsync(HttpMethod.Delete, "webresourceset(" + id.Value + ")");
        }

        /// <summary>Imports an unmanaged solution zip, overwriting unmanaged customizations.
        /// Retries when another solution operation is in flight.</summary>
        public async Task ImportSolutionAsync(byte[] zipBytes)
        {
            var body = new JObject
            {
                ["OverwriteUnmanagedCustomizations"] = true,
                ["PublishWorkflows"] = false,
                ["CustomizationFile"] = Convert.ToBase64String(zipBytes),
                ["ImportJobId"] = Guid.NewGuid().ToString()
            }.ToString();

            const int maxAttempts = 5;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await SendAsync(HttpMethod.Post, "ImportSolution", body);
                    return;
                }
                catch (DataverseApiException ex) when (attempt < maxAttempts && IsTransientSolutionBusy(ex))
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            }
        }

        private static bool IsTransientSolutionBusy(DataverseApiException ex)
        {
            return ex.Message.Contains("at the same time")
                || ex.Message.Contains("another")
                || ex.Message.Contains("try again later");
        }

        public void Dispose() => _http.Dispose();
    }

    public class DataverseApiException : Exception
    {
        public DataverseApiException(string message) : base(message) { }
    }
}
