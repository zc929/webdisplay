using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace WebDisplay.Services;

internal sealed class AdminRequest
{
    public int Version { get; set; } = 1;
    public string Operation { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Days { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

internal sealed class AdminResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// One-use, local-only IPC. Both ends authenticate the other process before
/// transmitting data. An explicit Administrators ACL supports over-the-shoulder
/// UAC credentials without allowing other standard users to open the pipe.
/// </summary>
internal static class AdminPipe
{
    private const string Prefix = "WebDisplay.Admin.";
    private const int MaxMessageBytes = 128 * 1024;

    internal static string ExecutablePath => Environment.ProcessPath
        ?? throw new InvalidOperationException(L.Text("无法确定程序文件路径，请从已发布的 EXE 启动程序。"));

    internal static async Task<string> SendAsync(AdminRequest request)
    {
        string name = Prefix + Guid.NewGuid().ToString("N");
        using NamedPipeServerStream pipe = CreateServer(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        Task connection = pipe.WaitForConnectionAsync(timeout.Token);
        Process? child = null;
        try
        {
            var start = new ProcessStartInfo(ExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "--admin " + name + " " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            };
            child = await Task.Run(() => Process.Start(start)).ConfigureAwait(false);
            if (child is null)
                throw new InvalidOperationException(L.Text("无法启动管理员配置程序。"));

            Task exited = child.WaitForExitAsync(timeout.Token);
            Task first = await Task.WhenAny(connection, exited).ConfigureAwait(false);
            if (first == exited && !connection.IsCompletedSuccessfully)
                throw new InvalidOperationException(L.Text("管理员配置程序在建立安全连接前已退出。"));
            await connection.ConfigureAwait(false);
            if (!AdminPipeNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientPid)
                || clientPid != (uint)child.Id)
                throw new InvalidOperationException(L.Text("管理员配置连接身份验证失败。"));

            await WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            AdminResponse response = await ReadAsync<AdminResponse>(pipe, timeout.Token).ConfigureAwait(false);
            if (!response.Success)
                throw new InvalidOperationException(LocalizeResponse(request, response));
            return LocalizeResponse(request, response);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException(L.Text("已取消管理员授权，系统设置未更改。"), ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException(L.Text("管理员配置等待超时。请重新打开设置检查系统状态后重试。"), ex);
        }
        finally
        {
            request.Password = string.Empty;
            timeout.Cancel();
            child?.Dispose();
        }
    }

    // The short-lived admin helper keeps its default zh-CN locale. Translate
    // only known response text in the authenticated client; IPC and credentials
    // remain unchanged, and native Windows error details retain the OS language.
    internal static string LocalizeResponse(AdminRequest request, AdminResponse response)
    {
        if (response.Success && request.Operation == "restart" && request.Enabled)
            return L.Format("定时重启已保存，将按电脑本地时间 {0} 执行。", request.Time);
        const string failurePrefix = "系统配置失败：";
        if (!response.Success && response.Message.StartsWith(failurePrefix, StringComparison.Ordinal))
            return L.Format("系统配置失败：{0}", L.Text(response.Message[failurePrefix.Length..]));
        return L.Text(response.Message);
    }

    internal static async Task<int> RunAsync(string[] args, Func<AdminRequest, string> apply)
    {
        if (args.Length != 3 || args[0] != "--admin" || !IsValidPipeName(args[1])
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid)
            || parentPid <= 0 || parentPid == Environment.ProcessId)
            return 2;

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return 5;

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!AdminPipeNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid)
                || serverPid != (uint)parentPid || !IsSameExecutable(parentPid))
                return 5;

            AdminRequest request = await ReadAsync<AdminRequest>(pipe, timeout.Token).ConfigureAwait(false);
            AdminResponse response;
            try
            {
                if (request.Version != 1 || (request.Operation != "restart" && request.Operation != "autologon"))
                    throw new InvalidOperationException(L.Text("不支持的管理员操作。"));
                response = new AdminResponse { Success = true, Message = apply(request) };
            }
            catch (Exception ex)
            {
                // Exception text contains only system errors and validation messages;
                // request bodies and credentials are never included in exceptions/logs.
                response = new AdminResponse { Success = false, Message = L.Format("系统配置失败：{0}", ex.Message) };
            }
            finally
            {
                request.Password = string.Empty;
            }
            await WriteAsync(pipe, response, timeout.Token).ConfigureAwait(false);
            return response.Success ? 0 : 1;
        }
        catch
        {
            // This helper has no UI or log: the calling application reports failure.
            return 1;
        }
    }

    private static bool IsValidPipeName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length != Prefix.Length + 32)
            return false;
        return Guid.TryParseExact(name.Substring(Prefix.Length), "N", out _);
    }

    private static bool IsSameExecutable(int processId)
    {
        using SafeProcessHandle process = AdminPipeNative.OpenProcess(0x1000, false, processId);
        if (process.IsInvalid)
            return false;
        var buffer = new System.Text.StringBuilder(32768);
        uint size = (uint)buffer.Capacity;
        return AdminPipeNative.QueryFullProcessImageName(process, 0, buffer, ref size)
            && string.Equals(Path.GetFullPath(buffer.ToString()), Path.GetFullPath(ExecutablePath), StringComparison.OrdinalIgnoreCase);
    }

    private static NamedPipeServerStream CreateServer(string name)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException(L.Text("无法获取当前 Windows 用户标识。"));
        string descriptor = "D:P(A;;GA;;;" + sid + ")(A;;GA;;;BA)(A;;GA;;;SY)";
        if (!AdminPipeNative.ConvertStringSecurityDescriptorToSecurityDescriptor(descriptor, 1, out IntPtr securityDescriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new AdminPipeNative.SecurityAttributes
            {
                Length = Marshal.SizeOf<AdminPipeNative.SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = false
            };
            // FIRST_PIPE_INSTANCE prevents name hijacking; REJECT_REMOTE_CLIENTS
            // prevents access over SMB. Overlapped IO permits bounded cancellation.
            SafePipeHandle handle = AdminPipeNative.CreateNamedPipe("\\\\.\\pipe\\" + name,
                0x00000003 | 0x40000000 | 0x00080000, 0x00000008,
                1, MaxMessageBytes, MaxMessageBytes, 0, ref attributes);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error);
            }
            try
            {
                return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            AdminPipeNative.LocalFree(securityDescriptor);
        }
    }

    private static async Task WriteAsync<T>(Stream pipe, T value, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            if (body.Length > MaxMessageBytes)
                throw new InvalidOperationException(L.Text("配置数据过长。"));
            byte[] length = BitConverter.GetBytes(body.Length);
            await pipe.WriteAsync(length, token).ConfigureAwait(false);
            await pipe.WriteAsync(body, token).ConfigureAwait(false);
            await pipe.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }

    private static async Task<T> ReadAsync<T>(Stream pipe, CancellationToken token)
    {
        byte[] lengthBytes = new byte[4];
        await pipe.ReadExactlyAsync(lengthBytes, token).ConfigureAwait(false);
        int length = BitConverter.ToInt32(lengthBytes);
        if (length <= 0 || length > MaxMessageBytes)
            throw new InvalidOperationException(L.Text("无效的管理员通信数据。"));
        byte[] body = new byte[length];
        try
        {
            await pipe.ReadExactlyAsync(body, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(body) ?? throw new InvalidOperationException(L.Text("管理员通信数据为空。"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }
}

internal static class AdminPipeNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] internal bool InheritHandle;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out IntPtr securityDescriptor, out uint size);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances,
        int outBufferSize, int inBufferSize, uint defaultTimeout, ref SecurityAttributes attributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder name, ref uint size);
}
