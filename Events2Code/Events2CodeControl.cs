using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Events2Code.Logic;
using McTools.Xrm.Connection;
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

        // ===== what is on the wire =====
        //
        // Everything the tool asks Dataverse is started by a click, and on a slow link every one
        // of those clicks is seconds wide. Nothing disables the window while it waits - the panel
        // XrmToolBox draws is a small box in the middle of the tab - so the tool has to know for
        // itself what is outstanding, and whose answer an arriving one is.

        /// <summary>
        /// Which question is current. Bumped by every new one and checked by every answer:
        /// browsing a list of tables asks about each in turn, and the slower answer to the
        /// earlier question must not land on top of the later one.
        /// </summary>
        private int _generation;

        private bool _loadingEntities;
        private bool _loadingForms;
        private bool _loadingHandlers;
        private bool _writing;
        private bool _savingFile;

        /// <summary>What went wrong with the last thing asked, or null. Outlives its dialog.</summary>
        private string _trouble;

        /// <summary>What the form on screen adds up to, once it has actually arrived.</summary>
        private string _handlerSummary = "";

        /// <summary>
        /// Whether anything is happening to the form on screen. Everything that reads the handler
        /// grid or rewrites the form is dead while this is true, because the grid is either empty
        /// or about to be replaced by an answer that has not arrived.
        /// </summary>
        private bool FormBusy
        {
            get { return _loadingForms || _loadingHandlers || _writing || _savingFile; }
        }

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

        // ===== the environment changing under the window =====

        /// <summary>
        /// A different environment, handed over by XrmToolBox.
        ///
        /// Everything on screen belongs to the org it was read from: the tables, the form opened
        /// out of them, the handlers in the grid and the code written from them. None of it is
        /// this org's, and the form is the dangerous one - its id is the thing Unregister and
        /// Generate write back to, and an id from one environment applied to another is a write
        /// nobody asked for. So the window lets go of the old org before it takes up the new one.
        ///
        /// The generation moves on for the same reason it moves on anywhere else here: whatever
        /// is still on the wire was asked of somewhere else, and its answer must land on nobody.
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail,
                                              string actionName, object parameter)
        {
            var busy = _loadingEntities || _loadingForms || _loadingHandlers || _writing;

            _generation++;
            _loadingEntities = false;
            _loadingForms = false;
            _loadingHandlers = false;
            _writing = false;
            _trouble = null;

            _entities = new List<EntityItem>();
            _selectedEntity = null;
            _selectedForm = null;
            _lvEntities.Items.Clear();
            _lvForms.Items.Clear();
            ClearForm();

            // Only what was cancelable is cancelled, and only when something was out: on a slow
            // link the rest of a question asked of an org nobody is looking at is worth stopping.
            if (busy) CancelWorker();

            // After the window has let go, not before. When a connection is made because
            // somebody pressed a button, it arrives carrying that method's name and the base
            // class runs it as its last act - so a reset placed after this call would throw away
            // the load the connection was made for. Nothing here asks XrmToolBox for a
            // connection today, but the ordering is not worth being wrong about later.
            base.UpdateConnection(newService, detail, actionName, parameter);

            UpdateState();
        }

        // ===== Entities =====

        private void BtnLoadEntities_Click(object sender, EventArgs e)
        {
            if (Service == null)
            {
                MessageBox.Show("Not connected to Dynamics. Please connect first.", "No Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var generation = StartFetch();
            _loadingEntities = true;
            UpdateState();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading tables...",
                // Every table in the environment is the heaviest request this tool makes, and on
                // a slow link it is most of a minute. The query on the wire cannot be recalled,
                // but waiting for it can be given up on - which without this was only possible by
                // closing the tab.
                IsCancelable = true,
                Work = (worker, args) =>
                {
                    var req = new RetrieveAllEntitiesRequest
                    {
                        EntityFilters = EntityFilters.Entity,
                        RetrieveAsIfPublished = true
                    };
                    var response = (RetrieveAllEntitiesResponse)Service.Execute(req);
                    if (worker.CancellationPending)
                    {
                        args.Cancel = true;
                        return;
                    }

                    args.Result = response;
                },
                PostWorkCallBack = result =>
                {
                    // The tab may have been closed while this was in the air, and on a slow link
                    // that is a window seconds wide rather than milliseconds. Nobody is owed an
                    // answer, a dialog least of all.
                    if (Landed(generation)) return;

                    _loadingEntities = false;

                    // Cancelled before Result, which rethrows after a cancel or an error.
                    if (result.Cancelled)
                    {
                        _trouble = "Cancelled. Press Load Tables to ask again.";
                        UpdateState();
                        return;
                    }

                    if (result.Error != null)
                    {
                        Blame("The tables could not be read", result.Error, "Error loading tables");
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
                    UpdateState();
                }
            });
        }

        /// <summary>
        /// Starts a new question, and says so: whatever is on its way belongs to the old one and
        /// its answer is nobody's when it lands. Returns the generation to check on arrival.
        /// </summary>
        private int StartFetch()
        {
            _trouble = null;
            return ++_generation;
        }

        /// <summary>
        /// Whether this answer has nowhere to go: the tab was closed while it was in the air, or
        /// somebody asked something else in the meantime and this is the reply to the older
        /// question. Either way it is not to be rendered, and not to be spoken about.
        /// </summary>
        private bool Landed(int generation)
        {
            return IsDisposed || generation != _generation;
        }

        /// <summary>
        /// Says what failed, twice: once in a dialog, which is read and dismissed, and once on the
        /// status line, which stays. An environment that could not be reached must not go on
        /// reading as an environment with nothing in it.
        /// </summary>
        private void Blame(string what, Exception error, string caption)
        {
            _trouble = what + ": " + error.Message;
            UpdateState();
            MessageBox.Show(error.Message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            // The table selection has already moved, so the forms of the one before it are no
            // longer an answer to anything on screen - and neither is the form opened out of
            // them. Both go now rather than when the new answer arrives, because the gap between
            // those two moments is the whole of what a slow link adds.
            var generation = StartFetch();
            _loadingForms = true;
            _lvForms.Items.Clear();
            _selectedForm = null;
            ClearForm();
            UpdateState();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading forms...",
                IsCancelable = true,
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
                    var found = Service.RetrieveMultiple(query);
                    if (worker.CancellationPending)
                    {
                        args.Cancel = true;
                        return;
                    }

                    args.Result = found;
                },
                PostWorkCallBack = result =>
                {
                    if (Landed(generation)) return;

                    _loadingForms = false;

                    if (result.Cancelled)
                    {
                        _trouble = "Cancelled. Click the table again to ask once more.";
                        UpdateState();
                        return;
                    }

                    if (result.Error != null)
                    {
                        // The list is already empty, and stays that way: a table whose forms
                        // could not be read must not read as a table with no forms either.
                        Blame("The forms could not be read", result.Error, "Error loading forms");
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

                    UpdateState();
                }
            });
        }

        private void LvForms_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvForms.SelectedItems.Count == 0) return;
            _selectedForm = (FormItem)_lvForms.SelectedItems[0].Tag;
            LoadFormXml(_selectedForm);
        }

        private void LoadFormXml(FormItem form)
        {
            // Same reasoning as the forms list: the grid, the code and the buttons over them all
            // describe the form clicked before this one, and a person reading them while this
            // answer is on its way is reading about the wrong form.
            var generation = StartFetch();
            _loadingHandlers = true;
            ClearForm();
            UpdateState();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading form XML...",
                IsCancelable = true,
                Work = (worker, args) =>
                {
                    var found = Service.Retrieve("systemform", form.Id, new ColumnSet("formxml"));
                    if (worker.CancellationPending)
                    {
                        args.Cancel = true;
                        return;
                    }

                    args.Result = found;
                },
                PostWorkCallBack = result =>
                {
                    if (Landed(generation)) return;

                    _loadingHandlers = false;

                    if (result.Cancelled)
                    {
                        _trouble = "Cancelled. Click the form again to ask once more.";
                        UpdateState();
                        return;
                    }

                    if (result.Error != null)
                    {
                        Blame("The form could not be read", result.Error, "Error loading form");
                        return;
                    }

                    var formXml = ((Entity)result.Result).GetAttributeValue<string>("formxml");

                    List<EventHandlerInfo> handlers;
                    try
                    {
                        handlers = FormXmlParser.Parse(formXml).Handlers;
                    }
                    catch (Exception ex)
                    {
                        Blame("The form XML could not be read", ex, "Error parsing form XML");
                        return;
                    }

                    _currentFormXml = formXml;
                    _handlers = handlers;
                    FillHandlersGrid();
                    UpdateState();

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
            // Held rather than written straight to the label: it is a sentence about a form that
            // has arrived, and while the next one is on its way the label has something truer to
            // say. UpdateState decides which of the two is showing.
            _handlerSummary = _handlers.Count == 0
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

        /// <summary>Everything on screen that was read out of one particular form.</summary>
        private void ClearForm()
        {
            _currentFormXml = null;
            _handlers = new List<EventHandlerInfo>();
            _lvHandlers.Items.Clear();
            _handlerSummary = "";
            _generatedCode = "";
            _txtCode.Clear();
        }

        /// <summary>
        /// The toolbar and the status line, made to agree with what is actually true. One place,
        /// called from every path in and out of a fetch, because the failure paths are where a
        /// button that disables itself gets left dead for the rest of the session.
        /// </summary>
        private void UpdateState()
        {
            _btnLoadEntities.Enabled = !_loadingEntities;

            var hasCode = _generatedCode.Length > 0;
            _btnGenerate.Enabled = !FormBusy && _handlers.Any(h => h.IsConvertible);
            _btnCopy.Enabled = !FormBusy && hasCode;
            _btnSave.Enabled = !FormBusy && hasCode;
            _btnUnregister.Enabled = !FormBusy && hasCode && _currentFormXml != null;

            _lblStatus.Text =
                _trouble != null ? _trouble :
                _loadingEntities ? "Loading tables..." :
                _loadingForms ? "Loading forms..." :
                _loadingHandlers ? "Loading form..." :
                _writing ? "Updating form..." :
                _savingFile ? "Saving..." :
                _handlerSummary;
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
            UpdateState();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string path;
            using (var dialog = new SaveFileDialog
            {
                Filter = "JavaScript files (*.js)|*.js|All files (*.*)|*.*",
                FileName = (_selectedEntity ?? "form") + "_events.js"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                path = dialog.FileName;
            }

            // A file dialog is as likely to be pointed at a share or a synced folder as at the
            // local disk, and both of those block for as long as the network takes to give up.
            // Nothing that can hang belongs on the thread that draws the window.
            var code = _generatedCode;
            _savingFile = true;
            UpdateState();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Saving " + Path.GetFileName(path) + "...",
                Work = (worker, args) => File.WriteAllText(path, code),
                PostWorkCallBack = result =>
                {
                    if (IsDisposed) return;

                    _savingFile = false;

                    if (result.Error != null)
                    {
                        Blame("The file could not be written", result.Error, "Error saving file");
                        return;
                    }

                    UpdateState();
                }
            });
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

            // Held down for the duration of its own write. Two presses would be two rewrites of
            // the same form, and the second takes as its "before" state a form the first has
            // already stripped - so the backup meant to be the way back is a copy of the damage.
            _writing = true;
            UpdateState();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Updating form and publishing...",
                // Deliberately not cancelable: a form update followed by a publish is not a
                // question that can be taken back halfway through.
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
                    // No generation check: unlike a fetch, a write that has happened has to be
                    // reported whatever has been clicked since. Only a closed tab excuses it.
                    if (IsDisposed) return;

                    _writing = false;

                    if (result.Error != null)
                    {
                        Blame("The form could not be updated", result.Error, "Error updating form");
                        return;
                    }

                    UpdateState();
                    MessageBox.Show("Form updated and published.\nBackup saved to:\n" + result.Result, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    // The form it wrote, not whatever is selected by now.
                    LoadFormXml(form);
                }
            });
        }
    }
}
