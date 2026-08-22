using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Events2Code.Harness;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Events2Code.SlowHarness
{
    /// <summary>One round trip, as it happened.</summary>
    public class Call
    {
        /// <summary>Position among every call of the run, from 1.</summary>
        public int Index;

        /// <summary>Position among the calls of this kind, from 1. What a scenario scripts against.</summary>
        public int Nth;

        /// <summary>tables, forms, formxml, update or publish.</summary>
        public string What;

        /// <summary>Which table or which form the question was about, where there is one.</summary>
        public string Detail;

        public DateTime Started;
        public DateTime Ended;

        public override string ToString()
        {
            return "#" + Index + " " + What
                   + (string.IsNullOrEmpty(Detail) ? "" : " (" + Detail + ")")
                   + " " + (int)(Ended - Started).TotalMilliseconds + "ms";
        }
    }

    /// <summary>
    /// The environment, at a distance. Answers the four questions the tool asks - every table,
    /// the forms of one table, the XML of one form, and the update-and-publish a write is - out
    /// of <see cref="Sample"/>, and takes as long over each as the scenario says it should.
    ///
    /// It is a fake environment rather than a fake anything higher up on purpose: the tool goes
    /// to Dataverse through IOrganizationService and nothing else, so this is the only seam that
    /// leaves all of the tool above it running for real.
    ///
    /// Every call is logged - what was asked, about what, when it started and when it ended - and
    /// the log is the assertion surface for the questions latency actually raises: was the same
    /// thing asked twice, was anything asked at all after the user gave up, did the answer that
    /// landed last belong to the question asked last, and did one press really write once.
    /// </summary>
    public class SlowService : IOrganizationService
    {
        private readonly object _lock = new object();
        private readonly List<Call> _calls = new List<Call>();
        private int _index;
        private readonly Dictionary<string, int> _perKind = new Dictionary<string, int>();

        /// <summary>The form XML as this environment now holds it, keyed by form id.</summary>
        private readonly Dictionary<Guid, string> _formXml;

        public readonly List<SampleEntity> Entities;
        public readonly List<SampleForm> Forms;

        /// <summary>Every form XML this environment was told to store, in order.</summary>
        public readonly List<string> Written = new List<string>();

        /// <summary>How long this call should take. The whole point of the harness.</summary>
        public Func<Call, int> Latency = call => 0;

        /// <summary>What this call should throw instead of answering, or null to answer.</summary>
        public Func<Call, Exception> Fails = call => null;

        public SlowService(List<SampleEntity> entities, List<SampleForm> forms)
        {
            Entities = entities;
            Forms = forms;
            _formXml = forms.ToDictionary(f => f.Id, f => f.Xml);
        }

        /// <summary>The sample environment: every table, its forms, and their handlers.</summary>
        public static SlowService Sampled()
        {
            return new SlowService(Sample.Entities(), Sample.AllForms());
        }

        public List<Call> Log()
        {
            lock (_lock) return _calls.ToList();
        }

        public List<Call> Log(string what)
        {
            return Log().Where(c => c.What == what).ToList();
        }

        /// <summary>
        /// Whether two calls ever overlapped in time. A tool that asks one question at a time
        /// puts one round trip on the wire at a time, and this is how that is checked rather
        /// than assumed.
        /// </summary>
        public bool Overlapped()
        {
            var log = Log().OrderBy(c => c.Started).ToList();
            for (var i = 1; i < log.Count; i++)
            {
                if (log[i].Started < log[i - 1].Ended) return true;
            }

            return false;
        }

        private Call Begin(string what, string detail)
        {
            var call = new Call { What = what, Detail = detail, Started = DateTime.UtcNow };

            lock (_lock)
            {
                call.Index = ++_index;
                int nth;
                _perKind.TryGetValue(what, out nth);
                _perKind[what] = call.Nth = nth + 1;
                _calls.Add(call);
            }

            return call;
        }

        /// <summary>
        /// The answer as it stood when the question arrived, and then the wait.
        ///
        /// That order is load bearing rather than tidy. A query is settled where the data is, and
        /// only the answer travels; a fake that slept first and read afterwards would hand a
        /// question asked three seconds ago the environment as it is now - which is precisely the
        /// confusion the stale scenarios exist to catch, quietly resolved in the harness's
        /// favour. A failure waits just as long: a link that is going to time out takes as long
        /// about it as one that is going to work.
        /// </summary>
        private T Answer<T>(Call call, Func<T> answer)
        {
            var failure = Fails(call);
            var result = failure == null ? answer() : default(T);

            var delay = Latency(call);
            if (delay > 0) Thread.Sleep(delay);

            lock (_lock) call.Ended = DateTime.UtcNow;
            if (failure != null) throw failure;
            return result;
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveAllEntitiesRequest)
            {
                return Answer(Begin("tables", null), () =>
                {
                    var response = new RetrieveAllEntitiesResponse();
                    response.Results = new ParameterCollection
                    {
                        { "EntityMetadata", Entities.Select(Metadata).ToArray() }
                    };
                    return (OrganizationResponse)response;
                });
            }

            var publish = request as PublishXmlRequest;
            if (publish != null)
            {
                var xml = publish.ParameterXml ?? "";
                return Answer(Begin("publish", Between(xml, "<entity>", "</entity>")),
                    () => (OrganizationResponse)new PublishXmlResponse());
            }

            throw new NotSupportedException("Nothing in the tool executes " + request.RequestName + ".");
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = query as QueryExpression;
            if (q == null) throw new NotSupportedException("The tool only issues QueryExpressions.");
            if (q.EntityName != "systemform")
                throw new NotSupportedException("Nothing in the tool queries " + q.EntityName + ".");

            var entity = Equal(q, "objecttypecode");
            var types = Values(q, "type").OfType<int>().ToList();

            return Answer(Begin("forms", entity), () =>
            {
                lock (_lock)
                {
                    var rows = Forms
                        .Where(f => f.Entity == entity && (types.Count == 0 || types.Contains(f.Type)))
                        .OrderBy(f => f.Name, StringComparer.Ordinal)
                        .Select(f =>
                        {
                            var e = new Entity("systemform", f.Id);
                            e["name"] = f.Name;
                            e["type"] = new OptionSetValue(f.Type);
                            e["ismanaged"] = f.IsManaged;
                            return e;
                        })
                        .ToList();

                    // One page. Paging is not something this tool does, and what the harness
                    // varies is how long a page takes to arrive.
                    return new EntityCollection(rows) { MoreRecords = false };
                }
            });
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (entityName != "systemform")
                throw new NotSupportedException("Nothing in the tool retrieves " + entityName + ".");

            var form = Forms.FirstOrDefault(f => f.Id == id);
            return Answer(Begin("formxml", form == null ? id.ToString() : form.Name), () =>
            {
                lock (_lock)
                {
                    var e = new Entity("systemform", id);
                    string xml;
                    e["formxml"] = _formXml.TryGetValue(id, out xml) ? xml : Sample.FormXml(new List<Logic.EventHandlerInfo>());
                    return e;
                }
            });
        }

        public void Update(Entity entity)
        {
            if (entity.LogicalName != "systemform")
                throw new NotSupportedException("Nothing in the tool updates " + entity.LogicalName + ".");

            var form = Forms.FirstOrDefault(f => f.Id == entity.Id);
            var xml = entity.GetAttributeValue<string>("formxml");
            Answer<object>(Begin("update", form == null ? entity.Id.ToString() : form.Name), () =>
            {
                lock (_lock)
                {
                    _formXml[entity.Id] = xml;
                    Written.Add(xml);
                }

                return null;
            });
        }

        /// <summary>
        /// The tables, as metadata. Both of the properties the tool reads are declared without a
        /// public setter, because outside a real environment nothing has any business inventing
        /// metadata - so a harness standing in for one reaches them the way it reaches everything
        /// else here.
        /// </summary>
        private static EntityMetadata Metadata(SampleEntity entity)
        {
            var metadata = new EntityMetadata();
            Set(metadata, "LogicalName", entity.LogicalName);

            var localized = new LocalizedLabel(entity.DisplayName, 1033);
            var label = new Microsoft.Xrm.Sdk.Label(localized, new[] { localized });
            if (label.UserLocalizedLabel == null) Set(label, "UserLocalizedLabel", localized);
            Set(metadata, "DisplayName", label);

            return metadata;
        }

        private static void Set(object target, string name, object value)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, any | BindingFlags.DeclaredOnly);
                var setter = property == null ? null : property.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(target, new[] { value });
                    return;
                }

                var backing = type.GetField("<" + name + ">k__BackingField", any)
                              ?? type.GetField("_" + char.ToLowerInvariant(name[0]) + name.Substring(1), any);
                if (backing != null)
                {
                    backing.SetValue(target, value);
                    return;
                }
            }

            throw new MissingMemberException(target.GetType().Name, name);
        }

        private static string Equal(QueryExpression query, string attribute)
        {
            var condition = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute);
            return condition == null || condition.Values.Count == 0 ? null : condition.Values[0] as string;
        }

        private static List<object> Values(QueryExpression query, string attribute)
        {
            var condition = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute);
            return condition == null ? new List<object>() : condition.Values.ToList();
        }

        private static string Between(string text, string open, string close)
        {
            var from = text.IndexOf(open, StringComparison.Ordinal);
            if (from < 0) return null;
            from += open.Length;
            var to = text.IndexOf(close, from, StringComparison.Ordinal);
            return to < 0 ? null : text.Substring(from, to - from);
        }

        public Guid Create(Entity entity) { throw new NotSupportedException("The tool never creates."); }
        public void Delete(string entityName, Guid id) { throw new NotSupportedException("The tool never deletes."); }
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotSupportedException("The tool never associates."); }
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotSupportedException("The tool never disassociates."); }
    }
}
