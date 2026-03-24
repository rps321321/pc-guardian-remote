namespace PCGuardianRemote;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "PCGuardianRemote_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("PC Guardian Remote is already running.", "PC Guardian Remote",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
