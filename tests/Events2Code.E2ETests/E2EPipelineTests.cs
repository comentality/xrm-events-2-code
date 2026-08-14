using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Events2Code.Logic;
using NUnit.Framework;

namespace Events2Code.E2ETests
{
    /// <summary>
    /// End-to-end suite: runs the tool's real Logic pipeline (parse -> generate -> rewrite ->
    /// update + publish) against the live test environment, using the "E2E Events Test"
    /// fixture form deployed by the E2EEventsTest solution.
    /// </summary>
    [TestFixture]
    public class E2EPipelineTests
    {
        private static readonly Guid FixtureFormId = new Guid("0e2e0001-aaaa-4bbb-8ccc-000000000001");
        private const string HandlersLibrary = "e2e_test_handlers.js";
        private const string BootstrapFunction = "E2E.Bootstrap.onLoad";
        private const string InternalOnLoadFunction = "AppCommon.Contact.Instance.form_onload";

        private DataverseClient _client;
        private string _fixtureFormXml;

        [OneTimeSetUp]
        public async Task ResetFixtureAndFetchForm()
        {
            _client = DataverseClient.Connect();
            await FixtureReset.RunAsync(_client);
            _fixtureFormXml = await _client.GetFormXmlAsync(FixtureFormId);
        }

        [OneTimeTearDown]
        public async Task RestoreFixture()
        {
            if (_client == null) return;
            try { await FixtureReset.RunAsync(_client); }
            finally { _client.Dispose(); }
        }

        private static List<EventHandlerInfo> E2EHandlers(ParsedForm parsed) =>
            parsed.Handlers.Where(h => h.FunctionName.StartsWith("E2ETest.")).ToList();

        [Test, Order(1)]
        public void Parse_FindsAllRegistrations()
        {
            var parsed = FormXmlParser.Parse(_fixtureFormXml);

            Assert.That(parsed.Libraries, Is.EqualTo(new[] { HandlersLibrary }));
            Assert.That(parsed.Handlers, Has.Count.EqualTo(8));

            AssertHandler(parsed, EventKind.FormOnLoad, "", InternalOnLoadFunction, enabled: true, ctx: false, parameters: "");
            AssertHandler(parsed, EventKind.FormOnLoad, "", "E2ETest.onFormLoad", enabled: true, ctx: true, parameters: "");
            AssertHandler(parsed, EventKind.FormOnLoad, "", "E2ETest.onFormLoadExtra", enabled: true, ctx: false, parameters: "\"hello\", 42");
            AssertHandler(parsed, EventKind.FormOnSave, "", "E2ETest.onSave", enabled: true, ctx: true, parameters: "");
            AssertHandler(parsed, EventKind.AttributeOnChange, "firstname", "E2ETest.onFirstNameChange", enabled: true, ctx: true, parameters: "");
            AssertHandler(parsed, EventKind.AttributeOnChange, "lastname", "E2ETest.onLastNameChange", enabled: false, ctx: true, parameters: "'test'");
            AssertHandler(parsed, EventKind.TabStateChange, "DETAILS_TAB", "E2ETest.onDetailsTabStateChange", enabled: true, ctx: false, parameters: "");
            AssertHandler(parsed, EventKind.GridOnLoad, "Subgrid_e2e", "E2ETest.onSubgridLoad", enabled: true, ctx: true, parameters: "");

            Assert.That(parsed.Handlers.All(h => h.LibraryName == HandlersLibrary || h.FunctionName == InternalOnLoadFunction),
                "All UI handlers should come from the fixture library");
        }

        /// <summary>
        /// The stock handler lives in &lt;InternalHandlers&gt;, so it must be listed but never
        /// offered for conversion: converting it would strip form logic Dynamics owns.
        /// </summary>
        [Test, Order(1)]
        public void Parse_MarksInternalHandlerAsNotConvertible()
        {
            var parsed = FormXmlParser.Parse(_fixtureFormXml);

            var stock = parsed.Handlers.Single(h => h.FunctionName == InternalOnLoadFunction);
            Assert.That(stock.IsInternal, Is.True, "the AppCommon handler sits in <InternalHandlers>");
            Assert.That(stock.IsConvertible, Is.False, "internal handlers must never be convertible");
            Assert.That(stock.KindDisplay, Is.EqualTo("onload (internal)"));

            Assert.That(E2EHandlers(parsed).All(h => !h.IsInternal && h.IsConvertible),
                "every designer-registered fixture handler stays convertible");
        }

        /// <summary>
        /// The UI cannot check an internal handler, but the rewriter refuses to remove one
        /// independently of that: pass every parsed handler in and the stock one survives.
        /// </summary>
        [Test, Order(1)]
        public void Rewrite_RefusesToRemoveInternalHandler()
        {
            var everything = FormXmlParser.Parse(_fixtureFormXml).Handlers;
            Assert.That(everything.Any(h => h.IsInternal), "fixture must contain an internal handler");

            var rewritten = FormXmlRewriter.Rewrite(_fixtureFormXml, everything, BootstrapFunction, FixtureReset.BootstrapWebResourceName);
            var parsed = FormXmlParser.Parse(rewritten);

            Assert.That(parsed.Handlers.Select(h => h.FunctionName),
                Is.EquivalentTo(new[] { InternalOnLoadFunction, BootstrapFunction }),
                "the internal handler survives even when explicitly passed for removal");
            Assert.That(parsed.Handlers.Single(h => h.FunctionName == InternalOnLoadFunction).IsInternal, Is.True);
        }

        /// <summary>Skipped handlers are still reported in the generated file, with the reason.</summary>
        [Test, Order(2)]
        public void Generate_ListsInternalHandlerAsNotConverted()
        {
            var everything = FormXmlParser.Parse(_fixtureFormXml).Handlers;

            var js = JsCodeGenerator.Generate(everything, BootstrapFunction, "contact", "E2E Events Test");

            Assert.That(js, Does.Contain("// NOT converted - left registered on the form:"));
            Assert.That(js, Does.Contain(InternalOnLoadFunction));
            Assert.That(js, Does.Contain("[internal handler, owned by Dynamics]"));
            Assert.That(js, Does.Not.Contain(InternalOnLoadFunction + "(executionContext);"),
                "the internal handler must not be called from the bootstrap");
        }

        private static void AssertHandler(ParsedForm parsed, EventKind kind, string target, string fn,
            bool enabled, bool ctx, string parameters)
        {
            var h = parsed.Handlers.SingleOrDefault(x => x.FunctionName == fn);
            Assert.That(h, Is.Not.Null, "handler missing: " + fn);
            Assert.That(h.Kind, Is.EqualTo(kind), fn + " kind");
            Assert.That(h.TargetName, Is.EqualTo(target), fn + " target");
            Assert.That(h.Enabled, Is.EqualTo(enabled), fn + " enabled");
            Assert.That(h.PassExecutionContext, Is.EqualTo(ctx), fn + " passExecutionContext");
            Assert.That(h.Parameters, Is.EqualTo(parameters), fn + " parameters");
        }

        [Test, Order(2)]
        public void Generate_ProducesCorrectBootstrap()
        {
            var handlers = E2EHandlers(FormXmlParser.Parse(_fixtureFormXml));
            Assert.That(handlers, Has.Count.EqualTo(7));

            var js = JsCodeGenerator.Generate(handlers, BootstrapFunction, "contact", "E2E Events Test");

            // Namespace declarations and bootstrap shell
            Assert.That(js, Does.Contain("var E2E = E2E || {};"));
            Assert.That(js, Does.Contain("E2E.Bootstrap = E2E.Bootstrap || {};"));
            Assert.That(js, Does.Contain(BootstrapFunction + " = function (executionContext) {"));

            // OnSave: exec context + no params registers the function reference directly
            Assert.That(js, Does.Contain("formContext.data.entity.addOnSave(E2ETest.onSave);"));

            // OnChange: ?. guards a field that is missing from this form variant
            Assert.That(js, Does.Contain("formContext.getAttribute(\"firstname\")?.addOnChange(E2ETest.onFirstNameChange);"));
            // Disabled handler is emitted commented out, with its parameters preserved in a wrapper
            Assert.That(js, Does.Contain("// (disabled in designer) formContext.getAttribute(\"lastname\")?.addOnChange(function (executionContext) { E2ETest.onLastNameChange(executionContext, 'test'); });"));

            // Tab state change; no exec context -> wrapper calls with no args
            Assert.That(js, Does.Contain("formContext.ui.tabs.get(\"DETAILS_TAB\")?.addTabStateChange(function (executionContext) { E2ETest.onDetailsTabStateChange(); });"));

            // Subgrid OnLoad
            Assert.That(js, Does.Contain("formContext.getControl(\"Subgrid_e2e\")?.addOnLoad(E2ETest.onSubgridLoad);"));

            // Every registration is a single statement - no hand-indented multi-line emit
            Assert.That(js, Does.Not.Contain("var tab_"));
            Assert.That(js, Does.Not.Contain("var grid_"));

            // Original form OnLoad handlers called directly from the bootstrap
            Assert.That(js, Does.Contain("E2ETest.onFormLoad(executionContext);"));
            Assert.That(js, Does.Contain("E2ETest.onFormLoadExtra(\"hello\", 42);"));
        }

        [Test, Order(3)]
        public async Task UnregisterCycle_RewritesFormInEnv()
        {
            var handlers = E2EHandlers(FormXmlParser.Parse(_fixtureFormXml));
            var js = JsCodeGenerator.Generate(handlers, BootstrapFunction, "contact", "E2E Events Test");

            // 1. The bootstrap web resource must exist before the form references it
            await _client.UpsertWebResourceAsync(FixtureReset.BootstrapWebResourceName, js);

            // 2-3. Rewrite, update the form, publish - exactly what BtnUnregister_Click does
            var newXml = FormXmlRewriter.Rewrite(_fixtureFormXml, handlers, BootstrapFunction, FixtureReset.BootstrapWebResourceName);
            await _client.SetFormXmlAsync(FixtureFormId, newXml);
            await _client.PublishEntityAsync("contact");

            // 4. Round-trip: the published form must contain only the internal handler + bootstrap
            var roundTripped = await _client.GetFormXmlAsync(FixtureFormId);
            var parsed = FormXmlParser.Parse(roundTripped);

            Assert.That(parsed.Handlers.Where(h => h.FunctionName.StartsWith("E2ETest.")), Is.Empty,
                "all UI-registered E2ETest handlers should be gone");
            Assert.That(parsed.Handlers.Select(h => h.FunctionName),
                Is.EquivalentTo(new[] { InternalOnLoadFunction, BootstrapFunction }));

            var bootstrap = parsed.Handlers.Single(h => h.FunctionName == BootstrapFunction);
            Assert.That(bootstrap.Kind, Is.EqualTo(EventKind.FormOnLoad));
            Assert.That(bootstrap.Enabled, Is.True);
            Assert.That(bootstrap.PassExecutionContext, Is.True);
            Assert.That(bootstrap.LibraryName, Is.EqualTo(FixtureReset.BootstrapWebResourceName));

            // The rewriter adds the bootstrap library and leaves the original one in place
            Assert.That(parsed.Libraries, Is.EquivalentTo(new[] { HandlersLibrary, FixtureReset.BootstrapWebResourceName }));

            // The now-empty onchange/onsave/tabstatechange/grid event elements were dropped
            Assert.That(roundTripped, Does.Not.Contain("onDetailsTabStateChange"));
            Assert.That(roundTripped, Does.Not.Contain("\"onsave\"").And.Not.Contain("'onsave'"));
        }
    }
}
