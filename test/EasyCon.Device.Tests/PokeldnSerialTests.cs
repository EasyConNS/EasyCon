using EasyDevice.Connection.Pokeldn;
using System.Collections.Concurrent;
using System.Text;

namespace EasyCon.Device.Tests;

[TestFixture]
public class PokeldnSerialTests
{
    [Test]
    public void Handshake_RetriesLostBootRequest_ThenAcceptsFragmentedStatus()
    {
        using FirmwarePort port = new() { LostStatusRequests = 1 };
        using PokeldnSerialTransport transport = new(port);
        PokeldnStatus status = transport.Connect(CancellationToken.None);
        Assert.That(status.Version, Is.EqualTo("1.3.0"));
        Assert.That(port.StatusRequests, Is.EqualTo(2));
    }

    [Test]
    public void ReportAndStop_SendGoldenPacketsAndConsumeAcks()
    {
        using FirmwarePort port = new();
        using PokeldnSerialTransport transport = new(port);
        transport.Connect(CancellationToken.None);
        transport.Stop(CancellationToken.None);
        transport.SendReport(Convert.FromHexString("0400088080808000"), CancellationToken.None);
        Assert.That(port.Writes[1], Is.EqualTo(Convert.FromHexString("A55A0200031316")));
        Assert.That(port.Writes[2], Is.EqualTo(Convert.FromHexString("A55A09000104000880808080000D")));
    }

    [TestCase((byte)255, "-1")]
    [TestCase((byte)254, "-2")]
    public void NegativeAck_IsAnError(byte result, string message)
    {
        using FirmwarePort port = new() { Result = result };
        using PokeldnSerialTransport transport = new(port);
        transport.Connect(CancellationToken.None);
        IOException? error = Assert.Throws<IOException>(() => transport.Stop(CancellationToken.None));
        Assert.That(error!.Message, Does.Contain(message));
    }

    [Test]
    public void MissingAck_TimesOut_AndCancellationStopsBootWait()
    {
        using FirmwarePort port = new() { DropAcks = true };
        using PokeldnSerialTransport transport = new(port);
        transport.Connect(CancellationToken.None);
        Assert.Throws<TimeoutException>(() => transport.Stop(CancellationToken.None));
        using FirmwarePort booting = new() { LostStatusRequests = 100 };
        using PokeldnSerialTransport waiting = new(booting);
        using CancellationTokenSource cancel = new(100);
        Assert.Throws<OperationCanceledException>(() => waiting.Connect(cancel.Token));
    }

    private sealed class FirmwarePort : IPokeldnSerialPort
    {
        private readonly ConcurrentQueue<byte> _incoming = new();
        public List<byte[]> Writes { get; } = [];
        public int LostStatusRequests { get; init; }
        public int StatusRequests { get; private set; }
        public byte Result { get; init; }
        public bool DropAcks { get; init; }

        public void Open() { }
        public void Dispose() { }
        public void DiscardInput() => _incoming.Clear();

        public void Write(byte[] bytes)
        {
            Writes.Add(bytes);
            byte[] reply;
            if (bytes[4] == 2)
            {
                if (++StatusRequests <= LostStatusRequests)
                    return;
                // Deliberately construct the firmware response independently of the production encoder.
                byte[] body = [0x82, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    .. Encoding.ASCII.GetBytes("1.3.0"), 0, 1, 0, 1, 0];
                reply = [0xA5, 0x5A, (byte)body.Length, 0, .. body, (byte)body.Sum(value => value)];
            }
            else
            {
                if (DropAcks)
                    return;
                reply = [0xA5, 0x5A, 2, 0, 0x81, Result, (byte)(0x81 + Result)];
            }
            foreach (byte value in reply)
                _incoming.Enqueue(value);
        }

        public int Read(byte[] buffer)
        {
            if (_incoming.TryDequeue(out byte value))
            {
                buffer[0] = value;
                return 1;
            }
            Thread.Sleep(10);
            throw new TimeoutException();
        }
    }
}