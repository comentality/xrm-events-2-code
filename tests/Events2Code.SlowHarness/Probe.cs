using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Events2Code.SlowHarness
{
    /// <summary>
    /// The control from the outside, which is where a user is. Everything a scenario presses is
    /// pressed for real - PerformClick, a row selected, a box ticked - and everything it reads is
    /// what is on screen at that moment.
    ///
    /// It gets there through reflection, the same trade the UI harness makes: the fields are
    /// private because nothing outside the control has any business with them, and widening them
    /// for a test bench would be a worse thing to do to the tool than this is.
    /// </summary>
    public class Probe
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly Events2CodeControl _control;

        public Probe(Events2CodeControl control)
        {
            _control = control;
        }

        public T Field<T>(string name)
        {
            var field = typeof(Events2CodeControl).GetField(name, Priv);
            if (field == null) throw new MissingFieldException("Events2CodeControl", name);
            return (T)field.GetValue(_control);
        }

        // ===== what is on screen =====

        public Button Load { get { return Field<Button>("_btnLoadEntities"); } }
        public Button Generate { get { return Field<Button>("_btnGenerate"); } }
        public Button Copy { get { return Field<Button>("_btnCopy"); } }
        public Button Save { get { return Field<Button>("_btnSave"); } }
        public Button Unregister { get { return Field<Button>("_btnUnregister"); } }
        public TextBox Filter { get { return Field<TextBox>("_txtFilter"); } }
        public TextBox BootstrapFunction { get { return Field<TextBox>("_txtBootstrapFn"); } }
        public TextBox BootstrapLibrary { get { return Field<TextBox>("_txtBootstrapLib"); } }

        public ListView Entities { get { return Field<ListView>("_lvEntities"); } }
        public ListView Forms { get { return Field<ListView>("_lvForms"); } }
        public ListView Handlers { get { return Field<ListView>("_lvHandlers"); } }

        public string Status { get { return Field<Label>("_lblStatus").Text; } }

        public string Code { get { return Field<string>("_generatedCode"); } }

        public List<string> EntityNames() { return Texts(Entities); }

        public List<string> FormNames() { return Texts(Forms); }

        /// <summary>The function names the grid is showing, which is what a form is recognised by.</summary>
        public List<string> HandlerFunctions()
        {
            return Handlers.Items.Cast<ListViewItem>()
                .Select(i => i.SubItems.Count > 2 ? i.SubItems[2].Text : "")
                .ToList();
        }

        private static List<string> Texts(ListView list)
        {
            return list.Items.Cast<ListViewItem>().Select(i => i.Text).ToList();
        }

        // ===== what a user does =====

        public void PressLoad() { Load.PerformClick(); }

        public void PressGenerate() { Generate.PerformClick(); }

        public void PressUnregister() { Unregister.PerformClick(); }

        public void PressCopy() { Copy.PerformClick(); }

        public void SelectEntity(string displayName) { Select(Entities, displayName); }

        public void SelectForm(string name) { Select(Forms, name); }

        /// <summary>Ticks every row the tool will let be ticked, exactly as clicking them would.</summary>
        public void CheckAllHandlers()
        {
            foreach (ListViewItem item in Handlers.Items) item.Checked = true;
        }

        private static void Select(ListView list, string text)
        {
            var row = list.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => string.Equals(i.Text, text, StringComparison.Ordinal));
            if (row == null)
                throw new InvalidOperationException("No row named " + text + " in " + list.Name + " ("
                                                    + string.Join(", ", Texts(list)) + ")");
            row.Selected = true;
            row.Focused = true;
        }

        /// <summary>
        /// What the panel's Cancel button does. Only reachable through reflection because the
        /// panel is XrmToolBox's, drawn over the tool by code the tool does not own.
        /// </summary>
        public void PressCancel()
        {
            var method = _control.GetType().GetMethod("CancelWorker",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("PluginControlBase", "CancelWorker");
            method.Invoke(_control, null);
        }
    }
}
