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

		public bool IsStarted => AcceptTcpClientAsyncCTS is not null;

		MainWindow MainWindow { get; init; }
		TcpListener? TcpListener = null;
		CancellationTokenSource? AcceptTcpClientAsyncCTS = null;

		public async void Start(int port) {
			if (IsStarted)
				return;

			Debug.WriteLine("[TCP] Starting TCP server");

			TcpListener = new(IPAddress.Any, port);
			AcceptTcpClientAsyncCTS = new();

			TcpListener.Start();

			while (true) {
				try {
					HandleClient(await TcpListener.AcceptTcpClientAsync(AcceptTcpClientAsyncCTS.Token));
				}
				catch (Exception ex) {
					Stop();
				}
			}
		}

		public void Stop() {
			if (!IsStarted)
				return;

			TcpListener?.Stop();
			TcpListener?.Dispose();

			AcceptTcpClientAsyncCTS?.Cancel();
			AcceptTcpClientAsyncCTS = null;
		}

		async void HandleClient(TcpClient client) {
			try {
				Debug.WriteLine("[TCP] Accepting new client");

				using var stream = client.GetStream();
				var buffer = new byte[4096];

				while (true) {
					// Reading
					do {
						await stream.ReadExactlyAsync(buffer, 0, MainWindow.RemotePacketSize);

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
