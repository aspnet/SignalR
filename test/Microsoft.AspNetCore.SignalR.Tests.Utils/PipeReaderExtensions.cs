﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace System.IO.Pipelines
{
    public static class PipeReaderExtensions
    {
        public static async Task<bool> WaitToReadAsync(this PipeReader pipeReader)
        {
            while (true)
            {
                var result = await pipeReader.ReadAsync();

                try
                {
                    if (!result.Buffer.IsEmpty)
                    {
                        return true;
                    }

                    if (result.IsCompleted)
                    {
                        return false;
                    }
                }
                finally
                {
                    // Consume nothing, just wait for everything
                    pipeReader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                }
            }
        }

        public static async Task<byte[]> ReadSingleAsync(this PipeReader pipeReader)
        {
            var result = await pipeReader.ReadAsync();

            try
            {
                return result.Buffer.ToArray();
            }
            finally
            {
                pipeReader.AdvanceTo(result.Buffer.End);
            }
        }

        public static async Task<byte[]> ReadAllAsync(this PipeReader pipeReader)
        {
            while (true)
            {
                var result = await pipeReader.ReadAsync();

                try
                {
                    if (result.IsCompleted)
                    {
                        return result.Buffer.ToArray();
                    }
                }
                finally
                {
                    // Consume nothing, just wait for everything
                    pipeReader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                }
            }
        }
    }
}
