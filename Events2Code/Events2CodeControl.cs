using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Events2Code.Logic;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using XrmToolBox.Extensibility;
using Label = System.Windows.Forms.Label;

namespace Events2Code
{
    public partial class Events2CodeControl : PluginControlBase
    {
        private class EntityItem
        {
            public string LogicalName;
            public string DisplayName;
        }

        private class FormItem
        {
            public Guid Id;
            public string Name;
            public int Type;
            public bool IsManaged;
        }

        private List<EntityItem> _entities = new List<EntityItem>();
        private FormItem _selectedForm;
        private string _selectedEntity;
        private string _currentFormXml;
        private List<EventHandlerInfo> _handlers = new List<EventHandlerInfo>();

        // Left panel
        private SplitContainer _mainSplit;
        private SplitContainer _leftSplit;
        private Button _btnLoadEntities;
        private TextBox _txtFilter;
        private ListView _lvEntities;
        private ListView _lvForms;

        // Right panel
        private Panel _toolbar;
        private TextBox _txtBootstrapFn;
        private TextBox _txtBootstrapLib;
        private Button _btnGenerate;
        private Button _btnCopy;
        private Button _btnSave;
        private Button _btnUnregister;
        private SplitContainer _rightSplit;
        private ListView _lvHandlers;
        private TextBox _txtCode;

        public Events2CodeControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 280,
                FixedPanel = FixedPanel.Panel1
            };

            // ===== LEFT: entities (top) + forms (bottom) =====
            _leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };

            var leftToolbar = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(5) };
            _btnLoadEntities = new Button { Text = "Load Tables", Location = new Point(5, 5), Width = 130, Height = 26 };
            _btnLoadEntities.Click += BtnLoadEntities_Click;
            _txtFilter = new TextBox { Location = new Point(5, 37), Width = 250 };
            _txtFilter.TextChanged += (s, e) => FillEntityList();
            leftToolbar.Controls.Add(_btnLoadEntities);
            leftToolbar.Controls.Add(_txtFilter);

            _lvEntities = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Font = new Font("Segoe UI", 9f)
            };
            _lvEntities.Columns.Add("Table", 150);
            _lvEntities.Columns.Add("Logical Name", 120);
            _lvEntities.SelectedIndexChanged += LvEntities_SelectedIndexChanged;

            _leftSplit.Panel1.Controls.Add(_lvEntities);
            _leftSplit.Panel1.Controls.Add(leftToolbar);

            _lvForms = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Font = new Font("Segoe UI", 9f)
            };
            _lvForms.Columns.Add("Form", 170);
            _lvForms.Columns.Add("Type", 90);
            _lvForms.SelectedIndexChanged += LvForms_SelectedIndexChanged;

            _leftSplit.Panel2.Controls.Add(_lvForms);

            _mainSplit.Panel1.Controls.Add(_leftSplit);

            // ===== RIGHT: toolbar + handlers grid (top) + code (bottom) =====
            _toolbar = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(5) };

            var lblFn = new Label { Text = "Bootstrap function:", Location = new Point(5, 9), AutoSize = true };
            _txtBootstrapFn = new TextBox { Location = new Point(115, 6), Width = 220, Text = "MyOrg.FormEvents.onLoad" };
            var lblLib = new Label { Text = "Web resource:", Location = new Point(345, 9), AutoSize = true };
            _txtBootstrapLib = new TextBox { Location = new Point(430, 6), Width = 220, Text = "new_/scripts/form_events.js" };

            _btnGenerate = new Button { Text = "Generate Code", Location = new Point(5, 34), Width = 110, Height = 26, Enabled = false };
            _btnGenerate.Click += BtnGenerate_Click;
            _btnCopy = new Button { Text = "Copy", Location = new Point(120, 34), Width = 60, Height = 26, Enabled = false };
            _btnCopy.Click += (s, e) => { if (_txtCode.Text.Length > 0) Clipboard.SetText(_txtCode.Text); };
            _btnSave = new Button { Text = "Save...", Location = new Point(185, 34), Width = 60, Height = 26, Enabled = false };
            _btnSave.Click += BtnSave_Click;
            _btnUnregister = new Button { Text = "Unregister UI Handlers", Location = new Point(255, 34), Width = 160, Height = 26, Enabled = false };
            _btnUnregister.Click += BtnUnregister_Click;

            _toolbar.Controls.AddRange(new Control[] { lblFn, _txtBootstrapFn, lblLib, _txtBootstrapLib, _btnGenerate, _btnCopy, _btnSave, _btnUnregister });

            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };

            _lvHandlers = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                Font = new Font("Segoe UI", 9f)
            };
            _lvHandlers.Columns.Add("Event", 110);
            _lvHandlers.Columns.Add("Target", 130);
            _lvHandlers.Columns.Add("Function", 220);
            _lvHandlers.Columns.Add("Library", 200);
            _lvHandlers.Columns.Add("Parameters", 120);
            _lvHandlers.Columns.Add("Ctx", 40);
            _lvHandlers.Columns.Add("Enabled", 60);
            _lvHandlers.ItemCheck += LvHandlers_ItemCheck;

            _txtCode = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5f)
            };

            _rightSplit.Panel1.Controls.Add(_lvHandlers);
            _rightSplit.Panel2.Controls.Add(_txtCode);

            _mainSplit.Panel2.Controls.Add(_rightSplit);
            _mainSplit.Panel2.Controls.Add(_toolbar);

            Controls.Add(_mainSplit);
            ResumeLayout(false);
        }

        // ===== Entities =====

        private void BtnLoadEntities_Click(object sender, EventArgs e)
        {
            if (Service == null)
            {
                MessageBox.Show("Not connected to Dynamics. Please connect first.", "No Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading tables...",
                Work = (worker, args) =>
                {
                    var req = new RetrieveAllEntitiesRequest
                    {
                        EntityFilters = EntityFilters.Entity,
                        RetrieveAsIfPublished = true
                    };
                    args.Result = (RetrieveAllEntitiesResponse)Service.Execute(req);
                },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        MessageBox.Show(result.Error.Message, "Error loading tables", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var resp = (RetrieveAllEntitiesResponse)result.Result;
                    _entities = resp.EntityMetadata
                        .Where(m => m.DisplayName?.UserLocalizedLabel != null)
                        .Select(m => new EntityItem
                        {
                            LogicalName = m.LogicalName,
                            DisplayName = m.DisplayName.UserLocalizedLabel.Label
                        })
                        .OrderBy(m => m.DisplayName)
                        .ToList();

                    FillEntityList();
                }
            });
        }

        private void FillEntityList()
        {
            var filter = _txtFilter.Text.Trim();
            _lvEntities.BeginUpdate();
            _lvEntities.Items.Clear();
            foreach (var entity in _entities)
            {
                if (filter.Length > 0 &&
                    entity.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entity.LogicalName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var item = new ListViewItem(entity.DisplayName) { Tag = entity };
                item.SubItems.Add(entity.LogicalName);
                _lvEntities.Items.Add(item);
            }
            _lvEntities.EndUpdate();
        }

        // ===== Forms =====

        private void LvEntities_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvEntities.SelectedItems.Count == 0) return;
            var entity = (EntityItem)_lvEntities.SelectedItems[0].Tag;
            _selectedEntity = entity.LogicalName;
            LoadForms(entity.LogicalName);
        }

        private void LoadForms(string logicalName)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading forms...",
                Work = (worker, args) =>
                {
                    var query = new QueryExpression("systemform")
                    {
                        ColumnSet = new ColumnSet("name", "type", "ismanaged"),
                        Criteria =
                        {
                            Conditions =
                            {
                                new ConditionExpression("objecttypecode", ConditionOperator.Equal, logicalName),
                                new ConditionExpression("type", ConditionOperator.In, new object[] { 2, 7 }) // main, quick create
                            }
                        },
                        Orders = { new OrderExpression("name", OrderType.Ascending) }
                    };
                    args.Result = Service.RetrieveMultiple(query);
                },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        MessageBox.Show(result.Error.Message, "Error loading forms", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var forms = ((EntityCollection)result.Result).Entities
                        .Select(f => new FormItem
                        {
                            Id = f.Id,
                            Name = f.GetAttributeValue<string>("name"),
                            Type = f.GetAttributeValue<OptionSetValue>("type")?.Value ?? 0,
                            IsManaged = f.GetAttributeValue<bool?>("ismanaged") ?? false
                        })
                        .ToList();

                    _lvForms.BeginUpdate();
                    _lvForms.Items.Clear();
                    foreach (var form in forms)
                    {
                        var item = new ListViewItem(form.Name) { Tag = form };
                        item.SubItems.Add(form.Type == 2 ? "Main" : "Quick Create");
                        _lvForms.Items.Add(item);
                    }
                    _lvForms.EndUpdate();

                    ClearResults();
                }
            });
        }

        private void LvForms_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvForms.SelectedItems.Count == 0) return;
            _selectedForm = (FormItem)_lvForms.SelectedItems[0].Tag;
            LoadFormXml();
        }

        private void LoadFormXml()
        {
            var form = _selectedForm;
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading form XML...",
                Work = (worker, args) =>
                {
                    args.Result = Service.Retrieve("systemform", form.Id, new ColumnSet("formxml"));
                },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        MessageBox.Show(result.Error.Message, "Error loading form", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _currentFormXml = ((Entity)result.Result).GetAttributeValue<string>("formxml");

                    try
                    {
                        _handlers = FormXmlParser.Parse(_currentFormXml).Handlers;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error parsing form XML", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    FillHandlersGrid();

                    _btnGenerate.Enabled = _handlers.Any(h => h.IsConvertible);
                    _btnUnregister.Enabled = false;
                    _btnCopy.Enabled = false;
                    _btnSave.Enabled = false;
                    _txtCode.Text = "";

                    if (form.IsManaged)
                        MessageBox.Show("This form is managed. Unregistering handlers will create an unmanaged customization on top of it.",
                            "Managed Form", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
        }

        private void FillHandlersGrid()
        {
            _lvHandlers.ItemCheck -= LvHandlers_ItemCheck;
            _lvHandlers.BeginUpdate();
            _lvHandlers.Items.Clear();
            foreach (var handler in _handlers)
            {
                var item = new ListViewItem(handler.KindDisplay)
                {
                    Tag = handler,
                    Checked = handler.IsConvertible && handler.Enabled
                };
                item.SubItems.Add(handler.TargetName);
                item.SubItems.Add(handler.FunctionName);
                item.SubItems.Add(handler.LibraryName);
                item.SubItems.Add(handler.Parameters);
                item.SubItems.Add(handler.PassExecutionContext ? "yes" : "no");
                item.SubItems.Add(handler.Enabled ? "yes" : "no");
                if (!handler.IsConvertible)
                    item.ForeColor = Color.Gray;
                _lvHandlers.Items.Add(item);
            }
            _lvHandlers.EndUpdate();
            _lvHandlers.ItemCheck += LvHandlers_ItemCheck;
        }

        private void LvHandlers_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            var handler = (EventHandlerInfo)_lvHandlers.Items[e.Index].Tag;
            if (!handler.IsConvertible)
                e.NewValue = CheckState.Unchecked;
        }

        private void ClearResults()
        {
            _selectedForm = null;
            _currentFormXml = null;
            _handlers = new List<EventHandlerInfo>();
            _lvHandlers.Items.Clear();
            _txtCode.Text = "";
            _btnGenerate.Enabled = false;
            _btnCopy.Enabled = false;
            _btnSave.Enabled = false;
            _btnUnregister.Enabled = false;
        }

        // ===== Generate =====

        private List<EventHandlerInfo> CheckedHandlers()
        {
            return _lvHandlers.Items.Cast<ListViewItem>()
                .Where(i => i.Checked)
                .Select(i => (EventHandlerInfo)i.Tag)
                .ToList();
        }

        private void BtnGenerate_Click(object sender, EventArgs e)
        {
            var selected = CheckedHandlers();
            if (selected.Count == 0)
            {
                MessageBox.Show("Check at least one handler to convert.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bootstrapFn = _txtBootstrapFn.Text.Trim();
            if (bootstrapFn.Length == 0)
            {
                MessageBox.Show("Enter a bootstrap function name.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _txtCode.Text = JsCodeGenerator.Generate(selected, bootstrapFn, _selectedEntity, _selectedForm?.Name ?? "");
            _btnCopy.Enabled = true;
            _btnSave.Enabled = true;
            _btnUnregister.Enabled = true;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "JavaScript files (*.js)|*.js|All files (*.*)|*.*",
                FileName = (_selectedEntity ?? "form") + "_events.js"
            })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                    File.WriteAllText(dialog.FileName, _txtCode.Text);
            }
        }

        // ===== Unregister =====

        private void BtnUnregister_Click(object sender, EventArgs e)
        {
            var selected = CheckedHandlers();
            if (selected.Count == 0 || _selectedForm == null || _currentFormXml == null) return;

            var bootstrapFn = _txtBootstrapFn.Text.Trim();
            var bootstrapLib = _txtBootstrapLib.Text.Trim();
            if (bootstrapFn.Length == 0 || bootstrapLib.Length == 0)
            {
                MessageBox.Show("Enter the bootstrap function name and its web resource name first.", "Missing bootstrap info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Events2Code", "backups");
            var message =
                "This will:\n\n" +
                "  1. Remove " + selected.Count + " checked handler(s) from the form \"" + _selectedForm.Name + "\"\n" +
                "  2. Register \"" + bootstrapFn + "\" (" + bootstrapLib + ") on form OnLoad\n" +
                "  3. Publish the entity\n\n" +
                "Make sure the web resource \"" + bootstrapLib + "\" exists and contains the generated code BEFORE doing this, or the form scripts will stop working.\n\n" +
                "A backup of the current form XML will be saved to:\n" + backupDir + "\n\nContinue?";

            if (MessageBox.Show(message, "Unregister UI Handlers", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var form = _selectedForm;
            var entity = _selectedEntity;
            var formXml = _currentFormXml;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Updating form and publishing...",
                Work = (worker, args) =>
                {
                    Directory.CreateDirectory(backupDir);
                    var safeName = string.Concat((entity + "_" + form.Name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                    var backupPath = Path.Combine(backupDir, safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml");
                    File.WriteAllText(backupPath, formXml);

                    var newXml = FormXmlRewriter.Rewrite(formXml, selected, bootstrapFn, bootstrapLib);

                    var update = new Entity("systemform", form.Id);
                    update["formxml"] = newXml;
                    Service.Update(update);

                    Service.Execute(new PublishXmlRequest
                    {
                        ParameterXml = "<importexportxml><entities><entity>" + entity + "</entity></entities></importexportxml>"
                    });

                    args.Result = backupPath;
                },
                PostWorkCallBack = result =>
                {
                    if (result.Error != null)
                    {
                        MessageBox.Show(result.Error.Message, "Error updating form", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    MessageBox.Show("Form updated and published.\nBackup saved to:\n" + result.Result, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LoadFormXml();
                }
            });
        }
    }
}
