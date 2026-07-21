using Pandora.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Pandora
{
    public class Logger : ILogger
    {
        private const uint _singleFileMaxSize = 1024 * 1024 * 10;
        private const LogLevel MaxLogLevel = LogLevel.Trace;
        private static string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"log");
        private readonly Channel<LogMessage> _channel;
        private readonly ChannelReader<LogMessage> _re;
        private readonly ChannelWriter<LogMessage> _wr;
        private const ushort _batchWriteCount = 10;
        private StreamWriter? _writer;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _consumeTask;
        public readonly static  ILogger Instance = new Logger();
        public FileInfo _currentLogFile =null!;
        private static readonly TimeSpan _forceFlushInterval = TimeSpan.FromSeconds(30);
        public Logger()
        {
            Directory.CreateDirectory(_logPath);
            CreateNewLogFile();
            _channel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(30)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
            _re = _channel.Reader;
            _wr = _channel.Writer;
            _consumeTask = Task.Run(Consume, _cts.Token);
        }
        public async Task Consume()
        {
            var batchBuffer = new List<LogMessage>(_batchWriteCount);
            int writeCount = 0;
            try
            {
                while (!_cts.IsCancellationRequested) 
                {
                    batchBuffer.Clear();
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    linkedCts.CancelAfter(_forceFlushInterval);

                    try
                    {
                        while (batchBuffer.Count < _batchWriteCount && await _re.WaitToReadAsync(linkedCts.Token))
                        {
                            while (_re.TryRead(out var item))
                            {
                                batchBuffer.Add(item);
                                if (batchBuffer.Count >= _batchWriteCount) break;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                    {
                        // 超时刷新，继续循环
                    }

                    if (batchBuffer.Count > 0)
                    {
                        writeCount++;
                        WriteBatch(batchBuffer, writeCount%3==0);
                        CheckFileRoll();
                    }
                }
                await FlushAllRemaining();
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        private void WriteBatch(List<LogMessage> batchBuffer,bool checkFile)
        {
            if (_writer is null) return;
            foreach (var log in batchBuffer)
            {
                var line = log.ToString();
                _writer.WriteLine(line);
            }
            _writer.Flush();
            if (checkFile)
            {
                CheckFileRoll();
            }
        }
        private async Task FlushAllRemaining()
        {
            var list = new List<LogMessage>();
            while (_channel.Reader.TryRead(out var item))
                list.Add(item);
            if (list.Count > 0)
                WriteBatch(list,false);
        }
        public void Dispose()
        {
            _cts.Cancel();
            _consumeTask.Wait(2000);
        }

        public void Log(LogLevel level, string text, string? fun = null)
        {
            if (level > MaxLogLevel) 
            {
                return;
            }
            _wr.TryWrite(new LogMessage(level, text, fun));
        }
        private void CreateNewLogFile()
        {
            
            string date = DateTime.Now.ToString("yyyyMMdd");
            string dir = Path.Combine(_logPath, date);
            Directory.CreateDirectory(dir);
            int idx = 1;
            string path;
            do
            {
                path = Path.Combine(dir, $"log_{idx}.log");
                idx++;
            } while (File.Exists(path) && new FileInfo(path).Length > _singleFileMaxSize);

            _writer?.Dispose();
            //_currentLogPath = path;
            _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough),Encoding.UTF8);
            _currentLogFile=new FileInfo(path);
        }
        private void CheckFileRoll()
        {
            _currentLogFile.Refresh();
            if (_currentLogFile.Length < _singleFileMaxSize) return;
            CreateNewLogFile();
        }
    }
}
