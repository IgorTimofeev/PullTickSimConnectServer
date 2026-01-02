using Pizda;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Pizda;

public class Serial {
	public Serial() {
		
	}

	readonly SerialPort SerialPort = new("COM5", 115200, Parity.None, 8, StopBits.One);
	readonly ConcurrentQueue<byte[]> PacketQueue = [];
	readonly AutoResetEvent WritingARE = new(false);

	public event Action<byte[]>? DataReceived = null;

	bool _IsConnectedPizda = false;
	public bool IsConnected => _IsConnectedPizda && SerialPort.IsOpen;

	public event Action? IsConnectedChanged;

	public void EnqueueWriting(byte[] packet) {
		if (!_IsConnectedPizda)
			return;

		PacketQueue.Enqueue(packet);
		WritingARE.Set();
	}

	async void OnDataReceived(object s, SerialDataReceivedEventArgs e) {
		try {
			if (SerialPort.BytesToRead > 0) {
				var buffer = new byte[SerialPort.BytesToRead];
				var bytesRead = await SerialPort.BaseStream.ReadAsync(buffer);
				DataReceived?.Invoke(buffer);
			}
		}
		catch (Exception ex) {
			Debug.WriteLine($"Serial reading exception: {ex.Message}");
		}
	}

	void SerialLifeCheckerThreadBody() {
		while (true) {
			try {
				lock (SerialPort) {
					if (_IsConnectedPizda && !SerialPort.IsOpen) {
						SerialPort.Open();

						IsConnectedChanged?.Invoke();
					}
				}
			}
			catch (Exception ex) {
				Debug.WriteLine($"Serial life checker exception: {ex.Message}");
			}
			finally {
				Thread.Sleep(2000);
			}
		}
	}

	async void SerialWriterBody() {
		while (true) {
			try {
				WritingARE.WaitOne();

				// While have packet
				while (PacketQueue.TryDequeue(out var packet)) {
					//Logger.Info($"Writing queue size: {PacketQueue.Count}");

					// Trying to send until success
					while (true) {
						if (SerialPort.IsOpen) {
							await SerialPort.BaseStream.WriteAsync(packet);
							break;
						}
						else {
							Debug.WriteLine("Serial writing delayed: port is not open");

							Thread.Sleep(1000);
						}
					}
				}
			}
			catch (Exception ex) {
				Debug.WriteLine($"Serial writing exception: {ex.Message}");

				Thread.Sleep(1000);
			}
			finally {
				WritingARE.Reset();
			}
		}
	}

	public void Start(string name) {
		SerialPort.PortName = name;
		SerialPort.DataReceived += OnDataReceived;

		new Thread(SerialLifeCheckerThreadBody) {
			Name = "SerialLifeChecker",
			IsBackground = true
		}
		.Start();

		new Thread(SerialWriterBody) {
			Name = "SerialWriter",
			IsBackground = true
		}
		.Start();
	}

	public void Connect() {
		if (_IsConnectedPizda)
			return;

		_IsConnectedPizda = true;

		try {
			lock (SerialPort) {
				SerialPort.Open();

				IsConnectedChanged?.Invoke();
			}
		}
		catch (Exception ex) {
			Debug.WriteLine($"Serial connect exception: {ex.Message}");
		}
	}

	public void Disconnect() {
		if (!_IsConnectedPizda)
			return;

		_IsConnectedPizda = false;

		try {
			lock (SerialPort) {
				SerialPort.Close();

				IsConnectedChanged?.Invoke();
			}
		}
		catch (Exception ex) {
			Debug.WriteLine($"Serial connect exception: {ex.Message}");
		}
	}

	public void ChangeName(string name) {
		try {
			lock (SerialPort) {
				SerialPort.Close();
				SerialPort.PortName = name;
				SerialPort.Open();
			}
		}
		catch (Exception ex) {
			Debug.WriteLine($"Serial changeName exception: {ex.Message}");
		}
	}
}
