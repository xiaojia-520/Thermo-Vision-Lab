using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BoxHost
{
    /// <summary>
    /// Minimal read-only Modbus TCP client.
    /// Only FC02, FC03 and FC04 are exposed; this class cannot issue write
    /// commands.
    /// </summary>
    public sealed class ModbusTcpClient : IDisposable
    {
        private readonly string host;
        private readonly int port;
        private readonly TimeSpan timeout;
        private readonly SemaphoreSlim requestLock =
            new SemaphoreSlim(1, 1);

        private TcpClient client;
        private NetworkStream stream;
        private ushort transactionId;
        private bool disposed;

        public ModbusTcpClient(
            string host,
            int port,
            TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException(
                    "Host is required.",
                    nameof(host));
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port));
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout));
            }

            this.host = host;
            this.port = port;
            this.timeout = timeout;
        }

        public bool IsConnected
        {
            get
            {
                return client != null &&
                       client.Connected &&
                       stream != null;
            }
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(
            byte unitId,
            ushort startAddress,
            ushort registerCount,
            CancellationToken cancellationToken)
        {
            return await ReadRegistersAsync(
                    unitId,
                    0x03,
                    startAddress,
                    registerCount,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<ushort[]> ReadInputRegistersAsync(
            byte unitId,
            ushort startAddress,
            ushort registerCount,
            CancellationToken cancellationToken)
        {
            return await ReadRegistersAsync(
                    unitId,
                    0x04,
                    startAddress,
                    registerCount,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<ushort[]> ReadRegistersAsync(
            byte unitId,
            byte functionCode,
            ushort startAddress,
            ushort registerCount,
            CancellationToken cancellationToken)
        {
            if (registerCount < 1 || registerCount > 125)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(registerCount));
            }

            byte[] payload = await ExecuteReadAsync(
                    unitId,
                    functionCode,
                    startAddress,
                    registerCount,
                    cancellationToken)
                .ConfigureAwait(false);

            int expectedBytes = registerCount * 2;
            if (payload.Length != expectedBytes)
            {
                throw new InvalidDataException(
                    "Unexpected register byte count.");
            }

            ushort[] values = new ushort[registerCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = ReadUInt16(
                    payload,
                    index * 2);
            }

            return values;
        }

        public async Task<bool[]> ReadDiscreteInputsAsync(
            byte unitId,
            ushort startAddress,
            ushort inputCount,
            CancellationToken cancellationToken)
        {
            if (inputCount < 1 || inputCount > 2000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputCount));
            }

            byte[] payload = await ExecuteReadAsync(
                    unitId,
                    0x02,
                    startAddress,
                    inputCount,
                    cancellationToken)
                .ConfigureAwait(false);

            int expectedBytes = (inputCount + 7) / 8;
            if (payload.Length != expectedBytes)
            {
                throw new InvalidDataException(
                    "Unexpected discrete input byte count.");
            }

            bool[] values = new bool[inputCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] =
                    (payload[index / 8] &
                     (1 << (index % 8))) != 0;
            }

            return values;
        }

        public void Disconnect()
        {
            CloseConnection();
        }

        private async Task<byte[]> ExecuteReadAsync(
            byte unitId,
            byte functionCode,
            ushort startAddress,
            ushort itemCount,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            await requestLock.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await EnsureConnectedAsync(cancellationToken)
                    .ConfigureAwait(false);

                ushort requestTransactionId =
                    unchecked(++transactionId);
                byte[] request = BuildReadRequest(
                    requestTransactionId,
                    unitId,
                    functionCode,
                    startAddress,
                    itemCount);

                await WriteWithTimeoutAsync(
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);

                return await ReadResponseAsync(
                        requestTransactionId,
                        unitId,
                        functionCode,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                CloseConnection();
                throw;
            }
            finally
            {
                requestLock.Release();
            }
        }

        private async Task EnsureConnectedAsync(
            CancellationToken cancellationToken)
        {
            if (IsConnected)
            {
                return;
            }

            CloseConnection();

            TcpClient newClient = new TcpClient();
            Task connectTask =
                newClient.ConnectAsync(host, port);

            try
            {
                await AwaitWithTimeoutAsync(
                        connectTask,
                        cancellationToken,
                        "Connecting to the Modbus device timed out.")
                    .ConfigureAwait(false);
            }
            catch
            {
                newClient.Close();
                throw;
            }

            client = newClient;
            client.NoDelay = true;
            stream = client.GetStream();
        }

        private async Task<byte[]> ReadResponseAsync(
            ushort expectedTransactionId,
            byte expectedUnitId,
            byte expectedFunctionCode,
            CancellationToken cancellationToken)
        {
            byte[] header = new byte[7];
            await ReadExactlyWithTimeoutAsync(
                    header,
                    cancellationToken)
                .ConfigureAwait(false);

            ushort transaction =
                ReadUInt16(header, 0);
            ushort protocol =
                ReadUInt16(header, 2);
            ushort length =
                ReadUInt16(header, 4);
            byte unitId = header[6];

            if (transaction != expectedTransactionId)
            {
                throw new InvalidDataException(
                    "Unexpected transaction identifier.");
            }

            if (protocol != 0)
            {
                throw new InvalidDataException(
                    "Unexpected Modbus protocol identifier.");
            }

            if (unitId != expectedUnitId)
            {
                throw new InvalidDataException(
                    "Unexpected Modbus unit identifier.");
            }

            if (length < 2 || length > 254)
            {
                throw new InvalidDataException(
                    "Invalid Modbus response length.");
            }

            byte[] body = new byte[length - 1];
            await ReadExactlyWithTimeoutAsync(
                    body,
                    cancellationToken)
                .ConfigureAwait(false);

            byte functionCode = body[0];
            if (functionCode ==
                (byte)(expectedFunctionCode | 0x80))
            {
                if (body.Length < 2)
                {
                    throw new InvalidDataException(
                        "Incomplete Modbus exception response.");
                }

                throw new ModbusProtocolException(
                    expectedFunctionCode,
                    body[1]);
            }

            if (functionCode != expectedFunctionCode)
            {
                throw new InvalidDataException(
                    "Unexpected Modbus function code.");
            }

            if (body.Length < 2 ||
                body[1] != body.Length - 2)
            {
                throw new InvalidDataException(
                    "Invalid Modbus response payload.");
            }

            byte[] payload = new byte[body[1]];
            Buffer.BlockCopy(
                body,
                2,
                payload,
                0,
                payload.Length);
            return payload;
        }

        private async Task WriteWithTimeoutAsync(
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            Task writeTask = stream.WriteAsync(
                buffer,
                0,
                buffer.Length,
                cancellationToken);

            await AwaitWithTimeoutAsync(
                    writeTask,
                    cancellationToken,
                    "Writing the Modbus request timed out.")
                .ConfigureAwait(false);
        }

        private async Task ReadExactlyWithTimeoutAsync(
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                Task<int> readTask = stream.ReadAsync(
                    buffer,
                    offset,
                    buffer.Length - offset,
                    cancellationToken);

                int read = await AwaitWithTimeoutAsync(
                        readTask,
                        cancellationToken,
                        "Reading the Modbus response timed out.")
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    throw new IOException(
                        "The Modbus device closed the connection.");
                }

                offset += read;
            }
        }

        private async Task AwaitWithTimeoutAsync(
            Task operation,
            CancellationToken cancellationToken,
            string timeoutMessage)
        {
            Task delayTask = Task.Delay(
                timeout,
                cancellationToken);
            Task completed = await Task.WhenAny(
                    operation,
                    delayTask)
                .ConfigureAwait(false);

            if (completed != operation)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();
                throw new TimeoutException(timeoutMessage);
            }

            await operation.ConfigureAwait(false);
        }

        private async Task<T> AwaitWithTimeoutAsync<T>(
            Task<T> operation,
            CancellationToken cancellationToken,
            string timeoutMessage)
        {
            Task delayTask = Task.Delay(
                timeout,
                cancellationToken);
            Task completed = await Task.WhenAny(
                    operation,
                    delayTask)
                .ConfigureAwait(false);

            if (completed != operation)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();
                throw new TimeoutException(timeoutMessage);
            }

            return await operation.ConfigureAwait(false);
        }

        private static byte[] BuildReadRequest(
            ushort requestTransactionId,
            byte unitId,
            byte functionCode,
            ushort startAddress,
            ushort itemCount)
        {
            byte[] request = new byte[12];
            WriteUInt16(
                request,
                0,
                requestTransactionId);
            WriteUInt16(request, 2, 0);
            WriteUInt16(request, 4, 6);
            request[6] = unitId;
            request[7] = functionCode;
            WriteUInt16(request, 8, startAddress);
            WriteUInt16(request, 10, itemCount);
            return request;
        }

        private static ushort ReadUInt16(
            byte[] buffer,
            int offset)
        {
            return (ushort)(
                (buffer[offset] << 8) |
                buffer[offset + 1]);
        }

        private static void WriteUInt16(
            byte[] buffer,
            int offset,
            ushort value)
        {
            buffer[offset] =
                (byte)(value >> 8);
            buffer[offset + 1] =
                (byte)(value & 0xFF);
        }

        private void CloseConnection()
        {
            if (stream != null)
            {
                stream.Dispose();
                stream = null;
            }

            if (client != null)
            {
                client.Close();
                client = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ModbusTcpClient));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CloseConnection();
            requestLock.Dispose();
        }
    }

    public sealed class ModbusProtocolException : IOException
    {
        public ModbusProtocolException(
            byte functionCode,
            byte exceptionCode)
            : base(
                "Modbus exception " +
                exceptionCode +
                " for function " +
                functionCode +
                ".")
        {
            FunctionCode = functionCode;
            ExceptionCode = exceptionCode;
        }

        public byte FunctionCode { get; }

        public byte ExceptionCode { get; }
    }
}
