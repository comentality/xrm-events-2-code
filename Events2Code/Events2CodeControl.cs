using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        // RichTextBox.Text normalises line breaks to LF, so keep the generated source verbatim
        // for Copy/Save and let the preview control hold only the coloured rendering of it.
        private string _generatedCode = "";

        private bool _splittersLaid;
        private bool _handlerPaneUserSized;
        private Font _italic;

        // Left panel
        private SplitContainer _mainSplit;
        private SplitContainer _leftSplit;
        private Button _btnLoadEntities;
        private TextBox _txtFilter;
        private ListView _lvEntities;
        private ListView _lvForms;

        // Right panel
        private TableLayoutPanel _toolbar;
        private TextBox _txtBootstrapFn;
        private TextBox _txtBootstrapLib;
        private Button _btnGenerate;
        private Button _btnCopy;
        private Button _btnSave;
        private Button _btnUnregister;
        private Label _lblStatus;
        private SplitContainer _rightSplit;
        private ListView _lvHandlers;
        private RichTextBox _txtCode;

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
                FixedPanel = FixedPanel.Panel1
            };

            // ===== LEFT: entities (top) + forms (bottom) =====
            _leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };

            var leftToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 37,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(5, 5, 5, 0)
            };
            leftToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            leftToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _btnLoadEntities = new Button
            {
                Text = "Load Tables",
                Width = 110,
                Height = 26,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 6, 0)
            };
            _btnLoadEntities.Click += BtnLoadEntities_Click;
            _txtFilter = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0) };
            _txtFilter.TextChanged += (s, e) => FillEntityList();

            leftToolbar.Controls.Add(_btnLoadEntities, 0, 0);
            leftToolbar.Controls.Add(_txtFilter, 1, 0);

            _lvEntities = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Font = new Font("Segoe UI", 9f)
            };
            _lvEntities.Columns.Add("Table");
            _lvEntities.Columns.Add("Logical Name");
            ShareWidthBetweenColumns(_lvEntities, 240, 0.56f, 0.44f);
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
            _lvForms.Columns.Add("Form");
            _lvForms.Columns.Add("Type");
            ShareWidthBetweenColumns(_lvForms, 220, 0.64f, 0.36f);
            _lvForms.SelectedIndexChanged += LvForms_SelectedIndexChanged;

            _leftSplit.Panel2.Controls.Add(_lvForms);

            _mainSplit.Panel1.Controls.Add(_leftSplit);

            // ===== RIGHT: toolbar + handlers grid (top) + code (bottom) =====
            _toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(5, 5, 5, 5)
            };
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblFn = new Label { Text = "Bootstrap function:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
            _txtBootstrapFn = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 12, 0), Text = "MyOrg.FormEvents.onLoad" };
            var lblLib = new Label { Text = "Web resource:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
            _txtBootstrapLib = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0), Text = "new_/scripts/form_events.js" };

            _toolbar.Controls.Add(lblFn, 0, 0);
            _toolbar.Controls.Add(_txtBootstrapFn, 1, 0);
            _toolbar.Controls.Add(lblLib, 2, 0);
            _toolbar.Controls.Add(_txtBootstrapLib, 3, 0);

            _btnGenerate = new Button { Text = "Generate Code", Width = 110, Height = 26, Enabled = false, Margin = new Padding(0, 0, 6, 0) };
            _btnGenerate.Click += BtnGenerate_Click;
            _btnCopy = new Button { Text = "Copy", Width = 60, Height = 26, Enabled = false, Margin = new Padding(0, 0, 6, 0) };
            _btnCopy.Click += (s, e) => { if (_generatedCode.Length > 0) Clipboard.SetText(_generatedCode); };
            _btnSave = new Button { Text = "Save...", Width = 60, Height = 26, Enabled = false, Margin = new Padding(0, 0, 16, 0) };
            _btnSave.Click += BtnSave_Click;
            _btnUnregister = new Button { Text = "Unregister UI Handlers", Width = 160, Height = 26, Enabled = false, Margin = new Padding(0) };
            _btnUnregister.Click += BtnUnregister_Click;
            // Wrapping rather than a fixed row: "Unregister UI Handlers" is wide enough that the
            // row does not fit beside the status once the tool is docked narrow, and a button
            // that falls to a second line beats one clipped in half.
            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 4, 0, 0)
            };
            buttonRow.Controls.AddRange(new Control[] { _btnGenerate, _btnCopy, _btnSave, _btnUnregister });
            _toolbar.Controls.Add(buttonRow, 0, 1);
            _toolbar.SetColumnSpan(buttonRow, 3);

            // Its own cell rather than the end of the button row: in the flow panel a long status
            // would be clipped by the buttons as soon as the tool got narrow.
            _lblStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(12, 4, 0, 0)
            };
            _toolbar.Controls.Add(_lblStatus, 3, 1);

            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };
            // SplitterMoving fires for a drag only; SplitterMoved would also fire for our own
            // sizing and the grid would stop fitting itself after the very first form.
            _rightSplit.SplitterMoving += (s, e) => _handlerPaneUserSized = true;

            _lvHandlers = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                ShowItemToolTips = true,
                Font = new Font("Segoe UI", 9f)
            };
            _lvHandlers.Columns.Add("Event");
            _lvHandlers.Columns.Add("Target");
            _lvHandlers.Columns.Add("Function");
            _lvHandlers.Columns.Add("Library");
            _lvHandlers.Columns.Add("Parameters");
            _lvHandlers.Columns.Add("Ctx", -1, HorizontalAlignment.Center);
            _lvHandlers.Columns.Add("Enabled", -1, HorizontalAlignment.Center);
            ShareWidthBetweenColumns(_lvHandlers, 800, 0.115f, 0.13f, 0.27f, 0.25f, 0.125f, 0.04f, 0.07f);
            _lvHandlers.ItemCheck += LvHandlers_ItemCheck;

            _italic = new Font(_lvHandlers.Font, FontStyle.Italic);

            _txtCode = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false,
                DetectUrls = false,
                BackColor = Color.White,
                Font = new Font("Consolas", 9.5f)
            };

            _rightSplit.Panel1.Controls.Add(_lvHandlers);
            _rightSplit.Panel2.Controls.Add(_txtCode);

            _mainSplit.Panel2.Controls.Add(_rightSplit);
            _mainSplit.Panel2.Controls.Add(_toolbar);

            Controls.Add(_mainSplit);
            ResumeLayout(false);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LaySplitters();
            SetCueBanner(_txtFilter, "Filter tables...");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _italic != null) _italic.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // Only until it takes: after that the splitters belong to whoever drags them.
            if (!_splittersLaid) LaySplitters();
        }

        /// <summary>
        /// Panel sizes are validated against the container's *current* size, and during
        /// InitializeComponent that is still the 150x100 default a SplitContainer starts at: a
        /// distance set there is silently shrunk to fit and a min size set there throws outright.
        /// Both have to wait until the tool has been handed its real size, which is Load unless
        /// XrmToolBox builds the tab while it is still hidden - hence the retry from OnSizeChanged.
        /// </summary>
        private void LaySplitters()
        {
            _splittersLaid =
                LaySplit(_mainSplit, 220, 380, Math.Min(360, (int)(_mainSplit.Width * 0.3))) &
                LaySplit(_leftSplit, 120, 80, (int)(_leftSplit.Height * 0.6)) &
                LaySplit(_rightSplit, 80, 120, (int)(_rightSplit.Height * 0.35));
        }

        private static bool LaySplit(SplitContainer split, int min1, int min2, int distance)
        {
            var span = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (span < min1 + min2 + split.SplitterWidth) return false;

            split.Panel1MinSize = min1;
            split.Panel2MinSize = min2;
            split.SplitterDistance = Math.Min(Math.Max(distance, min1), span - min2 - split.SplitterWidth);
            return true;
        }

        /// <summary>
        /// Detail columns are plain pixel widths, so a list narrower than their sum scrolls
        /// sideways and a wider one leaves dead space past the last column. Give each column a
        /// share of the list instead and redistribute it whenever the list is resized. Below
        /// <paramref name="minTotal"/> the shares would ellipsise every cell down to nothing, so
        /// the columns stop shrinking there and the list scrolls sideways as before.
        /// </summary>
        private static void ShareWidthBetweenColumns(ListView list, int minTotal, params float[] shares)
        {
            EventHandler apply = (s, e) =>
            {
                var available = Math.Max(list.ClientSize.Width - 4, minTotal);
                if (available <= 0) return;

                list.BeginUpdate();
                var used = 0;
                for (var i = 0; i < list.Columns.Count; i++)
                {
                    // The last column absorbs the rounding so the widths always add up exactly.
                    var width = i == list.Columns.Count - 1 ? available - used : (int)(available * shares[i]);
                    list.Columns[i].Width = width;
                    used += width;
                }
                list.EndUpdate();
            };

            list.ClientSizeChanged += apply;
            apply(list, EventArgs.Empty);
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            if (box.IsHandleCreated)
                SendMessage(box.Handle, EM_SETCUEBANNER, IntPtr.Zero, text);
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
                    _generatedCode = "";
                    _txtCode.Clear();

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
                item.SubItems.Add(Tick(handler.PassExecutionContext));
                item.SubItems.Add(Tick(handler.Enabled));

                // A handler the form has switched off starts unchecked and converting it would
                // bring dead code back to life, so it should read as inert at a glance rather than
                // differ from its neighbours by one word in the last column.
                if (!handler.Enabled)
                {
                    item.Font = _italic;
                    item.ToolTipText = "Registered on the form but disabled.";
                }
                if (!handler.IsConvertible)
                {
                    item.ForeColor = Color.Gray;
                    item.ToolTipText = "Cannot be converted: " + handler.SkipReason + ".";
                }
                _lvHandlers.Items.Add(item);
            }
            _lvHandlers.EndUpdate();
            _lvHandlers.ItemCheck += LvHandlers_ItemCheck;
            FitHandlerPane();

            var convertible = _handlers.Count(h => h.IsConvertible);
            _lblStatus.Text = _handlers.Count == 0
                ? "No handlers registered on this form."
                : _handlers.Count + " handler(s), " + convertible + " convertible";
        }

        private static string Tick(bool value)
        {
            return value ? "✓" : "";
        }

        /// <summary>
        /// A form usually has a handful of handlers, and leaving the grid at a fixed share of the
        /// height means staring at an empty half while the generated code is squeezed underneath.
        /// Size it to what it holds, within limits - but only until the splitter is dragged, after
        /// which the height is the user's and reloading a form should not take it back.
        /// </summary>
        private void FitHandlerPane()
        {
            if (_handlerPaneUserSized || !_splittersLaid) return;

            var wanted = 0;     // a form with no handlers shrinks to the minimum LaySplit enforces
            if (_lvHandlers.Items.Count > 0)
            {
                var first = _lvHandlers.Items[0].Bounds;
                wanted = first.Top                                      // column headers
                       + (_lvHandlers.Items.Count + 1) * first.Height   // rows, plus one of slack
                       + 8;

                // Docked narrow the columns stop shrinking and the grid scrolls sideways instead;
                // fitted this tightly, that scrollbar would sit on top of the last row.
                var columns = _lvHandlers.Columns.Cast<ColumnHeader>().Sum(c => c.Width);
                if (columns > _lvHandlers.ClientSize.Width)
                    wanted += SystemInformation.HorizontalScrollBarHeight;
            }

            LaySplit(_rightSplit, 80, 120, Math.Min(wanted, (int)(_rightSplit.Height * 0.55)));
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
            _lblStatus.Text = "";
            _generatedCode = "";
            _txtCode.Clear();
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

            _generatedCode = JsCodeGenerator.Generate(selected, bootstrapFn, _selectedEntity, _selectedForm?.Name ?? "");
            JsSyntaxHighlighter.Apply(_txtCode, _generatedCode);
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
                    File.WriteAllText(dialog.FileName, _generatedCode);
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
