using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PullTickSimConnectServer {
    public class TCP(MainWindow mainWindow) {
		bool _IsStarted = false;
		public bool IsStarted {
			get => _IsStarted;

			private set {
				if (value == _IsStarted)
					return;

				_IsStarted = value;

				MainWindow.Dispatcher.BeginInvoke(() => IsStartedChanged?.Invoke());
			}
		}

		public event Action? IsStartedChanged;

		MainWindow MainWindow { get; init; } = mainWindow;
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

			StopCTS?.Cancel();
			StopCTS = null;

			TcpListener?.Stop();
			TcpListener?.Dispose();
		}

		async void HandleClient(TcpClient client) {
			try {
				Debug.WriteLine("[TCP] Accepting new client");

				using var stream = client.GetStream();
				var buffer = new byte[4096];

				while (true) {
					
				}

			}
			catch (Exception ex) {
				Debug.WriteLine($"[TCP] Exception during client handling: {ex.Message}");
			}

			Debug.WriteLine("[TCP] Client disconnected");
		}

		
	}
}
