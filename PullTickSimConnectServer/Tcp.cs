using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PullTickSimConnectServer {
    public class Tcp  {
        public Tcp(MainWindow mainWindow) {
			MainWindow = mainWindow;
		}

		MainWindow MainWindow { get; init; }

		TcpListener TcpListener = new(IPAddress.Any, 25569);

		public void Start() {
			new Thread(() => {
				TcpListener.Start();

				while (true) {
					try {
						Debug.WriteLine("[TCP] Waiting for clients");

						var client = TcpListener.AcceptTcpClient();

						HandleClient(client);
					}
					catch (Exception ex) {

					}
				}
			}) {
				Name = "TCP server thread",
				IsBackground = true
			}.Start();
		}

		unsafe void HandleClient(TcpClient client) {
			new Thread(() => {
				Debug.WriteLine("[TCP] Got client");

				using var clientStream = client.GetStream();

				try {
					byte[] sendingBuffer = new byte[sizeof(RemotePacket)];
					byte[] receivingBuffer;

					while (true) {
						do {
							//Debug.WriteLine("[TCP] Reading remote packet");

							clientStream.ReadExactly(sendingBuffer, 0, sendingBuffer.Length);

							lock (MainWindow.PacketsSyncRoot) {
								MainWindow.RemotePacket = BytesToStruct<RemotePacket>(sendingBuffer);
							}

							//LogRemotePacket();
							MainWindow.HandleRemotePacket();
						}
						while (client.Available > 0);

						// Writing
						//Debug.WriteLine("[TCP] Sending aircraft packet");

						lock (MainWindow.PacketsSyncRoot) {
							receivingBuffer = StructToBytes(MainWindow.AircraftPacket);
						}

						clientStream.Write(receivingBuffer, 0, receivingBuffer.Length);

						clientStream.Flush();

						Thread.Sleep(1000 / 30);
					}

				}
				catch (Exception ex) {

				}
			}) {
				IsBackground = true
			}.Start();
		}

		public static unsafe byte[] StructToBytes<T>(T value) where T : unmanaged {
			var pointer = (byte*) &value;

			var bytes = new byte[sizeof(T)];

			for (int i = 0; i < sizeof(T); i++)
				bytes[i] = pointer[i];

			return bytes;
		}

		public static unsafe T BytesToStruct<T>(byte[] bytes) where T : unmanaged {
			T value = default;

			var pointer = (byte*) &value;

			for (int i = 0; i < sizeof(T); i++)
				pointer[i] = bytes[i];

			return value;
		}
	}
}
