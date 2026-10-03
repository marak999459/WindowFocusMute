using System;
using System.Windows.Forms;

namespace WindowFocusMute;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var engine = new MuteEngine();
        Application.Run(new MainForm(engine));
    }
}
