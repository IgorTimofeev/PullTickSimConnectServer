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

	SerialPort SerialPort = new("COM5", 115200, Parity.None, 8, StopBits.One);
	ConcurrentQueue<byte[]> PacketQueue = [];
	AutoResetEvent WritingARE = new(false);

	public event Action<byte[]>? DataReceived = null;

	public void EnqueueWriting(byte[] packet) {
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
			Debug.WriteLine("Serial reading exception");
		}
	}

	void SerialLifeCheckerThreadBody() {
		while (true) {
			try {
				if (!SerialPort.IsOpen) {
					SerialPort.Open();
				}
			}
			catch (Exception ex) {
				Debug.WriteLine("Serial life checker exception");
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
				Debug.WriteLine("Serial writing exception");

				Thread.Sleep(1000);
			}
			finally {
				WritingARE.Reset();
			}
		}
	}

	public void Start() {
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
}
