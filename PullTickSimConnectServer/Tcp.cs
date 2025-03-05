using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PullTickSimConnectServer {
    public class TCP  {
        public TCP(MainWindow mainWindow) {
			MainWindow = mainWindow;
		}

		bool _IsStarted = false;
		public bool IsStarted {
			get => _IsStarted;
			private set {
				if (value == _IsStarted)
					return;

				_IsStarted = value;

				IsStartedChanged?.Invoke();
			}
		}

		public event Action IsStartedChanged;

		MainWindow MainWindow { get; init; }
		TcpListener? TcpListener = null;
		CancellationTokenSource? StopCTS = null;

		public async void Start(int port) {
			if (IsStarted)
				return;

			try {
				Debug.WriteLine($"[TCP] Starting on port {port}");

				IsStarted = true;

				TcpListener = new(IPAddress.Any, port);
				StopCTS = new();

				TcpListener.Start();

				while (true) {
					HandleClient(await TcpListener.AcceptTcpClientAsync(StopCTS.Token));
				}
			}
			catch (OperationCanceledException) {
				
			}
			catch (Exception ex) {
				Debug.WriteLine($"[TCP] Exception during accept client: {ex.Message}");

				Stop();
			}
		}

		public void Stop() {
			if (!IsStarted)
				return;

			Debug.WriteLine("[TCP] Stopping");

			IsStarted = false;

			TcpListener?.Stop();
			TcpListener?.Dispose();

			StopCTS?.Cancel();
			StopCTS = null;
		}

		async void HandleClient(TcpClient client) {
			try {
				Debug.WriteLine("[TCP] Accepting new client");

				using var stream = client.GetStream();
				var buffer = new byte[4096];

				while (true) {
					// Reading
					do {
						await stream.ReadExactlyAsync(buffer, 0, MainWindow.RemotePacketSize, StopCTS!.Token);

						lock (MainWindow.PacketsSyncRoot) {
							MainWindow.RemotePacket = BytesToStruct<RemotePacket>(buffer);
						}

						MainWindow.HandleRemotePacket();
					}
					while (client.Available > 0);

					// Writing
					lock (MainWindow.PacketsSyncRoot) {
						StructToBytes(MainWindow.AircraftPacket, buffer);
					}

					stream.Write(buffer, 0, MainWindow.AircraftPacketSize);

					//Thread.Sleep(1000 / 30);
				}

			}
			catch (Exception ex) {
				Debug.WriteLine($"[TCP] Exception during client handling: {ex.Message}");
			}

			Debug.WriteLine("[TCP] Client disconnected");
		}

		static unsafe void StructToBytes<T>(T value, byte[] buffer) where T : unmanaged {
			var pointer = (byte*) &value;

			for (int i = 0; i < sizeof(T); i++)
				buffer[i] = pointer[i];
		}

		static unsafe T BytesToStruct<T>(byte[] buffer) where T : unmanaged {
			T value = default;

			var pointer = (byte*) &value;

			for (int i = 0; i < sizeof(T); i++)
				pointer[i] = buffer[i];

			return value;
		}
	}
}
