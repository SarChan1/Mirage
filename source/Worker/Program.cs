using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Mirage Process Worker")]
[assembly: AssemblyDescription("Processo auxiliar experimental do Mirage")]
[assembly: AssemblyVersion("1.0.0.0")]

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        int parentId; long parentStarted; Guid session;
        const string prefix = "Local\\Mirage.Process.";
        if (args.Length != 5 || (args[3] != "window" && args[3] != "background") || (args[4] != "pt-BR" && args[4] != "en" && args[4] != "es") || !int.TryParse(args[0], out parentId) || parentId <= 0 || !long.TryParse(args[1], out parentStarted)
            || !args[2].StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(args[2].Substring(prefix.Length), out session)) return 64;
        try {
            using (var parent = Process.GetProcessById(parentId))
            using (var stop = EventWaitHandle.OpenExisting(args[2])) {
                if (parent.StartTime.ToUniversalTime().Ticks != parentStarted) return 65;
                if (args[3] == "window") {
                    Application.EnableVisualStyles();
                    using (var form = new WorkerWindow())
                    using (var timer = new System.Windows.Forms.Timer()) {
                        form.Text = System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath) + " · Mirage";
                        form.ClientSize = new System.Drawing.Size(340, 110);
                        form.BackColor = System.Drawing.Color.FromArgb(16, 21, 28);
                        form.ForeColor = System.Drawing.Color.FromArgb(200, 245, 120);
                        form.FormBorderStyle = FormBorderStyle.FixedSingle; form.MaximizeBox = false;
                        form.WindowState = FormWindowState.Minimized;
                        string message = args[4] == "en" ? "Mirage helper process\n\nYou can leave this window minimized.\nClosing it stops this process." : args[4] == "es" ? "Proceso auxiliar de Mirage\n\nPuedes dejar esta ventana minimizada.\nAl cerrarla se detiene este proceso." : "Processo auxiliar do Mirage\n\nPode deixar esta janela minimizada.\nFechar a janela encerra este processo.";
                        form.Controls.Add(new Label { Text = message, AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(18), Font = new System.Drawing.Font("Segoe UI", 10) });
                        timer.Interval = 750;
                        timer.Tick += delegate {
                            try { parent.Refresh(); if (stop.WaitOne(0) || parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != parentStarted) form.Close(); }
                            catch { form.Close(); }
                        };
                        timer.Start(); Application.Run(form);
                    }
                    return 0;
                }
                while (!stop.WaitOne(750)) {
                    parent.Refresh();
                    if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != parentStarted) break;
                }
            }
            return 0;
        } catch { return 66; }
    }
    private sealed class WorkerWindow : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
    }
}
