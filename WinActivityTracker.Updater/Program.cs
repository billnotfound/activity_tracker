namespace WinActivityTracker.Updater;

static class Program
{
    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        try
        {
            var engine = new UpdateEngine();
            return engine.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("============================================");
            Console.Error.WriteLine($"未处理的异常: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Console.Error.WriteLine("============================================");

            try
            {
                MessageBox.Show(
                    $"更新过程中发生未处理的错误：\n\n{ex.Message}",
                    "自动更新 - 错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }

            return 2;
        }
    }
}
