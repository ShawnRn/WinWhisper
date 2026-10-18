using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MemoAI.App;

// Windows process loopback includes the selected process tree (browser audio often lives in children).
// API: https://learn.microsoft.com/windows/win32/api/audioclientactivationparams/
public sealed class ProcessAudioCapture(uint processId) : IWaveIn
{
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? capture;
    public WaveFormat WaveFormat { get; set; } = new(44100, 16, 2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;
    public void StartRecording() => capture = Task.Run(CaptureAsync);
    public Task WaitUntilReadyAsync() => ready.Task;
    public void StopRecording() => stop.Cancel();
    private async Task CaptureAsync()
    {
        IAudioClient? client = null; IAudioCaptureClient? packets = null;
        Exception? failure = null;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) throw new NotSupportedException("按 App 内录需要 Windows 11 或更新版本。");
            var callback = new Completion();
            var activation = Marshal.AllocHGlobal(12);
            try
            {
                Marshal.WriteInt32(activation, 0, 1); Marshal.WriteInt32(activation, 4, unchecked((int)processId)); Marshal.WriteInt32(activation, 8, 0);
                var variant = new PropVariant { Type = 65, Size = 12, Data = activation };
                var iid = typeof(IAudioClient).GUID;
                Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, ref variant, callback, out var operation));
                // Keep activation data alive until the OS completion callback has consumed it.
                var instance = await callback.Result.Task;
                GC.KeepAlive(operation); client = (IAudioClient)instance;
            }
            finally { Marshal.FreeHGlobal(activation); }
            var format = new WaveFormatEx { Tag = 1, Channels = 2, SampleRate = 44100, AverageBytes = 176400, Align = 4, Bits = 16 };
            Marshal.ThrowExceptionForHR(client.Initialize(0, 0x88020000, 10000000, 0, ref format, IntPtr.Zero));
            var captureId = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(client.GetService(ref captureId, out var service)); packets = (IAudioCaptureClient)service;
            Marshal.ThrowExceptionForHR(client.Start()); ready.TrySetResult();
            while (!stop.IsCancellationRequested)
            {
                Marshal.ThrowExceptionForHR(packets.GetNextPacketSize(out var next));
                while (next > 0)
                {
                    Marshal.ThrowExceptionForHR(packets.GetBuffer(out var pointer, out var frames, out var flags, out _, out _));
                    try
                    {
                        var buffer = new byte[checked((int)frames * 4)];
                        if ((flags & 2) == 0) Marshal.Copy(pointer, buffer, 0, buffer.Length);
                        DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, buffer.Length));
                    }
                    finally { Marshal.ThrowExceptionForHR(packets.ReleaseBuffer(frames)); }
                    Marshal.ThrowExceptionForHR(packets.GetNextPacketSize(out next));
                }
                await Task.Delay(10, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { failure = ex; ready.TrySetException(ex); }
        finally
        {
            if (client is not null) { _ = client.Stop(); }
            if (packets is not null) Marshal.ReleaseComObject(packets);
            if (client is not null) Marshal.ReleaseComObject(client);
            RecordingStopped?.Invoke(this, new StoppedEventArgs(failure));
        }
    }
    public void Dispose() { stop.Cancel(); capture?.GetAwaiter().GetResult(); stop.Dispose(); }

    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public uint Size; [FieldOffset(16)] public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormatEx { public ushort Tag, Channels; public uint SampleRate, AverageBytes; public ushort Align, Bits, Extra; }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, ref PropVariant parameters, ICompletion completion, out IOperation operation);
    [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICompletion { [PreserveSig] int ActivateCompleted(IOperation operation); }
    [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IAgile { }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Completion : ICompletion, IAgile
    {
        public readonly TaskCompletionSource<object> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActivateCompleted(IOperation operation)
        {
            try { Marshal.ThrowExceptionForHR(operation.GetActivateResult(out var result, out var instance)); Marshal.ThrowExceptionForHR(result); Result.TrySetResult(instance); }
            catch (Exception ex) { Result.TrySetException(ex); }
            return 0;
        }
    }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOperation { [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object instance); }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int share, uint flags, long duration, long periodicity, ref WaveFormatEx format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int share, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
        [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset(); [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong counter);
        [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
