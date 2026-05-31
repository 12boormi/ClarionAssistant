using System;
using System.Windows.Forms;
using ICSharpCode.Core;
using ClarionAssistant.Services;

namespace ClarionAssistant
{
    /// <summary>
    /// Modern Embeditor — Path A POC trigger.
    /// Toolbar/menu command that runs the completion POC against the active
    /// embeditor and reports where the result file was written.
    /// Registered on the embeditor toolbar path /SoftVelocity/Clarion/ToolBar/EmbedEditor.
    /// </summary>
    public class EmbeditorCompletionTestCommand : AbstractMenuCommand
    {
        public override void Run()
        {
            try
            {
                string result = EmbeditorCompletionService.RunCompletionTest();
                // Brief confirmation; full detail is in the result file + the editor popup.
                MessageBox.Show(
                    result + "\r\n\r\n(Result also written to: " + EmbeditorCompletionService.ResultFilePath + ")",
                    "Embeditor Completion POC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Completion POC failed: " + ex.Message,
                    "Embeditor Completion POC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
