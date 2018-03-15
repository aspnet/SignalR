namespace System.IO.Pipelines
{
    public static class ReadResultExtensions
    {
        public static void ThrowIfCanceled(this ReadResult readResult)
        {
            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }
    }
}