using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using auto_patcher;
using LegendaryExplorerCore;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;

namespace auto_patcher_gui
{
    public partial class AutoPatcherForm : Form
    {
        public AutoPatcherForm()
        {
            InitializeComponent();
        }

        private void AutoPatcherForm_Load(object sender, EventArgs e)
        {
            var le2Path = LE2Directory.GetBioGamePath();
            if (le2Path != null && Directory.Exists(le2Path))
            {
                folderBrowserDialog1.SelectedPath = le2Path;
                textBox1.Text = le2Path;
            }

            var documentPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var modDir = Path.Combine(documentPath, "ME3TweaksModManager", "mods", "LE2", "LE2-Liara_Squadmate",
                "DLC_MOD_LiaraSquad", "CookedPCConsole");
            if (Directory.Exists(modDir))
            {
                folderBrowserDialog2.SelectedPath = modDir;
                textBox2.Text = modDir;
            }

            validateActivateButton();

            batchCountField.Value = AutoPatcherLib.GetDefaultBatchCount();
        }

        private void applyButton_Click(object sender, EventArgs e)
        {
            applyButton.Enabled = false;

            var gameDir = textBox1.Text;
            var modDir = textBox2.Text;

            if (string.IsNullOrEmpty(gameDir)
                || !Directory.Exists(gameDir)
                || !"BioGame".Equals(Path.GetFileName(gameDir)))
            {
                MessageBox.Show(
                    $"'{gameDir}' is not a valid LE2 installation directory, be sure to select the ME2/BioGame folder.",
                    "Error",
                    MessageBoxButtons.OK
                );
                applyButton.Enabled = true;
                return;
            }

            if (string.IsNullOrEmpty(modDir)
                || !Directory.Exists(modDir)
                || !"CookedPCConsole".Equals(Path.GetFileName(modDir)))
            {
                MessageBox.Show(
                    $"'{modDir}' is not a valid LE2 Liara Squadmate mod directory, be sure to select the CookedPCConsole folder.",
                    "Error",
                    MessageBoxButtons.OK
                );
                applyButton.Enabled = true;
                return;
            }

            outputBox.Clear();

            Task.Run(() =>
            {
                var messageReporter = new MessageReporter(this);
                var progressListener = new ProgressListener(this);
                var batchCountVal = Convert.ToInt32(batchCountField.Value);
                var batchCount = batchCountVal > 0 ? batchCountVal : AutoPatcherLib.GetDefaultBatchCount();
                var adjustMountPriority = checkBox1.Checked;

                LegendaryExplorerCoreLib.InitLib(
                    null,
                    s => messageReporter.ReportError($"Failed to save package, {s}")
                );
                var autoPatcherLib = new AutoPatcherLib(messageReporter, progressListener);

                try
                {
                    autoPatcherLib.HandleGameDir(gameDir, modDir, batchCount, adjustMountPriority);
                }
                catch (Exception exception)
                {
                    ShowMessageBox("Error", $"Exception occurred while applying operations: {exception}");
                    return;
                }
                finally
                {
                    SetApplyButtonEnabled();
                    UpdateProgressBar(bar =>
                    {
                        bar.Minimum = 0;
                        bar.Value = 0;
                    });
                    SetStateLabelText("Ready");
                }

                if (messageReporter.ReportedErrors.IsEmpty() && messageReporter.ReportedWarnings.IsEmpty())
                {
                    ShowMessageBox("Success", "All changes have been applied successfully");
                }
                else
                {
                    ShowCollectedErrorsForm(messageReporter.ReportedErrors, messageReporter.ReportedWarnings);
                }
            });
        }

        private delegate void ShowCollectedErrorsFormDelegate(
            List<string> reportedErrors,
            List<string> reportedWarnings
        );

        private void ShowCollectedErrorsForm(List<string> reportedErrors, List<string> reportedWarnings)
        {
            if (InvokeRequired)
            {
                Invoke(new ShowCollectedErrorsFormDelegate(ShowCollectedErrorsForm), reportedErrors, reportedWarnings);
            }
            else
            {
                new CollectedErrorsForm(reportedErrors, reportedWarnings).ShowDialog();
            }
        }

        delegate void ShowMessageBoxDelegate(string caption, string text);

        private void ShowMessageBox(string caption, string text)
        {
            if (InvokeRequired)
            {
                Invoke(new ShowMessageBoxDelegate(ShowMessageBox), caption, text);
            }
            else
            {
                MessageBox.Show(
                    text,
                    caption,
                    MessageBoxButtons.OK
                );
            }
        }

        private void textChangedEventHandler(object sender, EventArgs e)
        {
            validateActivateButton();
        }

        private void validateActivateButton()
        {
            applyButton.Enabled = !string.IsNullOrEmpty(textBox1.Text) && !string.IsNullOrEmpty(textBox2.Text);
        }

        private void folderBrowserDialog1_HelpRequest(object sender, EventArgs e)
        {
        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {
        }

        private void label1_Click(object sender, EventArgs e)
        {
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                textBox1.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog2.ShowDialog() == DialogResult.OK)
            {
                textBox2.Text = folderBrowserDialog2.SelectedPath;
            }
        }

        private void label2_Click(object sender, EventArgs e)
        {
        }

        private void label3_Click(object sender, EventArgs e)
        {
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
        }

        delegate void AppendOutputTextDelegate(string text);

        private void AppendOutputText(string text)
        {
            if (outputBox.InvokeRequired)
            {
                Invoke(new AppendOutputTextDelegate(AppendOutputText), text);
            }
            else
            {
                outputBox.AppendText(text);
            }
        }

        delegate void SetStateLabelTextDelegate(string text);

        private void SetStateLabelText(string text)
        {
            if (stateLabel.InvokeRequired)
            {
                Invoke(new SetStateLabelTextDelegate(SetStateLabelText), text);
            }
            else
            {
                stateLabel.Text = text;
            }
        }

        delegate void UpdateProgressBarDelegate(Action<ProgressBar> updateAction);

        private void UpdateProgressBar(Action<ProgressBar> updateAction)
        {
            if (progressBar1.InvokeRequired)
            {
                Invoke(new UpdateProgressBarDelegate(UpdateProgressBar), updateAction);
            }
            else
            {
                updateAction.Invoke(progressBar1);
            }
        }

        delegate void SetEnabledCallback();

        public void SetApplyButtonEnabled()
        {
            if (applyButton.InvokeRequired)
            {
                Invoke(new SetEnabledCallback(SetApplyButtonEnabled));
            }
            else
            {
                applyButton.Enabled = true;
            }
        }

        public class MessageReporter : IMessageReporter
        {
            public readonly List<string> ReportedErrors = new();
            public readonly List<string> ReportedWarnings = new();

            private readonly AutoPatcherForm _autoPatcherForm;

            public MessageReporter(AutoPatcherForm autoPatcherForm)
            {
                _autoPatcherForm = autoPatcherForm;
            }

            public void ReportException(Exception e, string msg)
            {
                ReportedErrors.Add($"{msg}: {e.Message}");
                _autoPatcherForm.AppendOutputText($"ERROR: {msg}: {e}{Environment.NewLine}");
            }

            public void ReportError(string msg)
            {
                ReportedErrors.Add(msg);
                _autoPatcherForm.AppendOutputText($"ERROR: {msg}{Environment.NewLine}");
            }

            public void ReportWarning(string msg)
            {
                ReportedWarnings.Add(msg);
                _autoPatcherForm.AppendOutputText($"WARN: {msg}{Environment.NewLine}");
            }

            public void ReportInformation(string msg)
            {
                _autoPatcherForm.AppendOutputText($"INFO: {msg}{Environment.NewLine}");
            }
        }

        public class ProgressListener : IProgressEventListener
        {
            private readonly AutoPatcherForm _autoPatcherForm;

            public ProgressListener(AutoPatcherForm autoPatcherForm)
            {
                _autoPatcherForm = autoPatcherForm;
            }

            public void OnScanning()
            {
                _autoPatcherForm.UpdateProgressBar(bar => bar.Style = ProgressBarStyle.Marquee);
            }

            public void OnStart(int itemCount)
            {
                _autoPatcherForm.UpdateProgressBar(progressBar =>
                {
                    progressBar.Style = ProgressBarStyle.Blocks;
                    progressBar.Minimum = 1;
                    progressBar.Maximum = itemCount;
                    progressBar.Step = 1;
                });
            }

            public void OnStageStart(string stageName)
            {
                _autoPatcherForm.SetStateLabelText(stageName);
            }

            public void OnStepCompleted()
            {
                _autoPatcherForm.UpdateProgressBar(bar => bar.PerformStep());
            }
        }

        private void stateLabel_Click(object sender, EventArgs e)
        {
        }
    }

    public class AutoGrowLabel : Label
    {
        public AutoGrowLabel()
        {
            AutoSize = false;
        }

        private void ResizeLabel()
        {
            var size = new Size(Width, Int32.MaxValue);
            size = TextRenderer.MeasureText(Text, Font, size, TextFormatFlags.WordBreak);
            Height = size.Height + Padding.Vertical;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            ResizeLabel();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ResizeLabel();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ResizeLabel();
        }
    }
}