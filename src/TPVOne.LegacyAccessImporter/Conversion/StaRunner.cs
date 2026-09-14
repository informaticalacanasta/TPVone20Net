using System.Runtime.ExceptionServices;

namespace TPVOne.LegacyAccessImporter.Conversion;

internal static class StaRunner
{
    public static void Run(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });
        thread.IsBackground = false;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Name = "AccessCOM";
        thread.Start();
        thread.Join();
        if (captured is not null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }
}
