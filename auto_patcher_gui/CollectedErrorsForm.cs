using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace auto_patcher_gui
{
    public partial class CollectedErrorsForm : Form
    {
        private readonly List<string> errors;
        private readonly List<string> warnings;

        public CollectedErrorsForm(List<string> errors, List<string> warnings)
        {
            InitializeComponent();
            this.errors = errors;
            this.warnings = warnings;
        }

        private void CollectedErrorForm_Load(object sender, EventArgs e)
        {
            foreach (var error in errors)
            {
                listBox1.Items.Add($"ERROR: {error}");
            }

            foreach (var warning in warnings)
            {
                listBox1.Items.Add(warning);
            }
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
        }

        delegate void listBox1_KeyDownDelegate(object sender, KeyEventArgs e);

        private void listBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new listBox1_KeyDownDelegate(listBox1_KeyDown), sender, e);
            }
            else
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    var copyBuffer = new StringBuilder();

                    foreach (var selectedItem in listBox1.SelectedItems)
                    {
                        copyBuffer.AppendLine(selectedItem.ToString());
                    }

                    if (copyBuffer.Length > 0)
                    {
                        Clipboard.SetText(copyBuffer.ToString());
                    }
                }
            }
        }
    }
}