using Microsoft.FlightSimulator.SimConnect;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PullTickSimConnectServer;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SimData {
	public double Latitude;
	public double Longitude;

	public double Pitch;
	public double Yaw;
	public double Roll;
	public double SlipAndSkid;

	public double Altitude;
	public double AirSpeed;

	public double Pressure;
	public double Temperature;

	public double WindDirectionDegrees;
	public double WindSpeedKnots;

}

public enum SimDataRequest {
	Request1
}

public enum SimDefinition {
	SimData
}

public enum SimEvent {
	AILERON_SET,
	RUDDER_SET,
	ELEVATOR_SET,

	THROTTLE1_SET,
	THROTTLE2_SET,

	FLAPS_SET,
	SPOILERS_SET,

	GEAR_SET,

	AP_SPD_VAR_SET,
	HEADING_BUG_SET,
	AP_ALT_VAR_SET_ENGLISH,

	FLIGHT_LEVEL_CHANGE_ON,
	FLIGHT_LEVEL_CHANGE_OFF,

	AP_HDG_HOLD_ON,
	AP_HDG_HOLD_OFF
}

public enum SimNotificationGroup {
	Group0
}

public class Sim {
	public Sim(MainWindow mainWindow) {
		MainWindow = mainWindow;

		ReconnectTimer = new(
			_ => {
				Debug.WriteLine($"[Sim] Restarting by timer");

				if (!IsStarted)
					Start();
			},
			null,
			Timeout.InfiniteTimeSpan,
			Timeout.InfiniteTimeSpan
		);

		ReconnectTimerStop();
	}

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

	bool _IsConnected = false;
	public bool IsConnected {
		get => _IsConnected;
		private set {
			if (value == _IsConnected)
				return;

			_IsConnected = value;

			MainWindow.Dispatcher.BeginInvoke(() => IsConnectedChanged?.Invoke());
		}
	}

	public event Action? IsStartedChanged, IsConnectedChanged;

	readonly MainWindow MainWindow;
	SimConnect? SimConnect = null;
	Thread? Thread = null;

	static readonly TimeSpan ReconnectTimerPerdiod = TimeSpan.FromSeconds(5);
	readonly Timer ReconnectTimer;

	object SyncRoot { get; init; } = new();

	public void Start() {
		if (IsStarted)
			return;

		try {
			Debug.WriteLine("[Sim] Starting");

			IsStarted = true;
			ReconnectTimerStop();

			// throw new Exception("Test");

			// If MSFS is not running, calling new SimConnect() will throw COMException
			SimConnect = new(
				"Managed Data Request",
				IntPtr.Zero,
				0x0402,
				null,
				0
			);

			SimConnect!.OnRecvOpen += new(OnRecvOpen);
			SimConnect.OnRecvQuit += new(OnRecvQuit);
			SimConnect.OnRecvException += new(OnRecvException);
			SimConnect.OnRecvSimobjectDataBytype += new(OnRecvSimobjectDataBytype);

			// Throttle 1-2
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "PLANE LATITUDE", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "PLANE LONGITUDE", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

			SimConnect.AddToDataDefinition(SimDefinition.SimData, "PLANE PITCH DEGREES", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "PLANE HEADING DEGREES TRUE", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "PLANE BANK DEGREES", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "TURN COORDINATOR BALL", "Position 128", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

			SimConnect.AddToDataDefinition(SimDefinition.SimData, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "AIRSPEED INDICATED", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

			SimConnect.AddToDataDefinition(SimDefinition.SimData, "BAROMETER PRESSURE", "millibars", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "AMBIENT TEMPERATURE", "celsius", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

			SimConnect.AddToDataDefinition(SimDefinition.SimData, "AMBIENT WIND DIRECTION", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
			SimConnect.AddToDataDefinition(SimDefinition.SimData, "AMBIENT WIND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

			SimConnect.RegisterDataDefineStruct<SimData>(SimDefinition.SimData);

			Thread = new(() => {
				try {
					Debug.WriteLine("[Sim] Receiving messages");

					while (true) {
						lock (SyncRoot) {
							SimConnect!.ReceiveMessage();

							SimConnect.RequestDataOnSimObjectType(
								SimDataRequest.Request1,
								SimDefinition.SimData,
								0,
								SIMCONNECT_SIMOBJECT_TYPE.USER
							);
						}

						Thread.Sleep(1000 / 30);
					}
				}
				catch (ThreadInterruptedException) {

				}
				catch (Exception ex) {
					Debug.WriteLine($"[Sim] Exception during receiving messages: {ex.Message}");

					ScheduleRestart();
				}
			}) {
				IsBackground = true
			};

			Thread.Start();
		}
		catch (Exception ex) {
			Debug.WriteLine($"[Sim] Exception during starting: {ex.Message}");

			ScheduleRestart();
		}
	}

	void PrepareForStop() {
		IsStarted = false;
		IsConnected = false;

		Thread?.Interrupt();
		Thread = null;

		SimConnect?.Dispose();
		SimConnect = null;
	}

	public void Stop() {
		if (!IsStarted)
			return;

		Debug.WriteLine("[Sim] Stopping");

		PrepareForStop();
		ReconnectTimerStop();
	}

	void ScheduleRestart() {
		Debug.WriteLine($"[Sim] Scheduling restart in {ReconnectTimerPerdiod}");

		PrepareForStop();
		ReconnectTimerStart();
	}

	void ReconnectTimerStart() {
		ReconnectTimer!.Change(ReconnectTimerPerdiod, ReconnectTimerPerdiod);
	}

	void ReconnectTimerStop() {
		ReconnectTimer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	// ----------------------------------------- Callbacks -----------------------------------------

	void OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data) {

	}

	void OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data) {
		IsConnected = true;
	}

	void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data) {
		Stop();
	}

	void OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data) {
		if (data.dwRequestID == 0) {
			MainWindow.HandleAircraftPacket((SimData) data.dwData[0]);
		}
		else {
			Debug.WriteLine($"Unknown request ID: {data.dwRequestID}");
		}
	}

	// ----------------------------------------- Generic events -----------------------------------------

	public void TransmitEvent(Enum eventID, uint value) {
		lock (SyncRoot) {
			SimConnect?.MapClientEventToSimEvent(eventID, eventID.ToString());
			SimConnect?.TransmitClientEvent(0U, eventID, value, SimNotificationGroup.Group0, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
		}
	}

	public void TransmitEvent(Enum eventID) {
		TransmitEvent(eventID, 0);
	}

	public void TransmitEventEX1(Enum eventID, uint value) {
		lock (SyncRoot) {
			SimConnect?.MapClientEventToSimEvent(eventID, eventID.ToString());
			SimConnect?.TransmitClientEvent_EX1(0U, eventID, SimNotificationGroup.Group0, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY, value, 0, 0, 0, 0);
		}
	}

	public void SendMinMax16383Event(Enum eventID, float percent) {
		const float min = -16383f;
		const float max = 16383f;

		percent = min + percent * (max - min);

		var bytes = BitConverter.GetBytes(Convert.ToInt32(percent));
		var pizda = BitConverter.ToUInt32(bytes);

		TransmitEvent(eventID, pizda);
	}

	public void SendMinMax16383Event(Enum eventID, ushort value) {
		SendMinMax16383Event(eventID, (float) (value / 65535f));
	}

	public void SendMax16383Event(Enum eventID, ushort value) {
		TransmitEvent(eventID, (uint) Math.Round(value / 65535f * 16384f));
	}

	public void SendEvent(Enum eventID, bool value) {
		TransmitEvent(eventID, (uint) (value ? 1 : 0));
	}

	// ----------------------------------------- Exact events -----------------------------------------

	public void SendAileronsEvent(ushort value) {
		SendMinMax16383Event(SimEvent.AILERON_SET, value);
	}

	public void SendGearSetEvent(bool value) {
		SendEvent(SimEvent.GEAR_SET, value);
	}

	public void SendElevatorEvent(ushort value) {
		SendMinMax16383Event(SimEvent.ELEVATOR_SET, value);
	}

	public void SendRudderEvent(ushort value) {
		SendMinMax16383Event(SimEvent.RUDDER_SET, value);
	}

	public void SendThrottle1Event(ushort value) {
		SendMax16383Event(SimEvent.THROTTLE1_SET, value);
	}

	public void SendThrottle2Event(ushort value) {
		SendMax16383Event(SimEvent.THROTTLE2_SET, value);
	}

	public void SendFlapsEvent(ushort value) {
		SendMax16383Event(SimEvent.FLAPS_SET, value);
	}

	public void SendSpoilersEvent(ushort value) {
		SendMax16383Event(SimEvent.SPOILERS_SET, value);
	}

	public void SendSpeedEvent(uint value) {
		TransmitEventEX1(SimEvent.AP_SPD_VAR_SET, value);
	}

	public void SendHeadingEvent(uint value) {
		TransmitEventEX1(SimEvent.HEADING_BUG_SET, value);
	}

	public void SendAltitudeEvent(uint value) {
		TransmitEventEX1(SimEvent.AP_ALT_VAR_SET_ENGLISH, value);
	}

	public void SendAPFLCEvent(bool value) {
		TransmitEvent(value ? SimEvent.FLIGHT_LEVEL_CHANGE_ON : SimEvent.FLIGHT_LEVEL_CHANGE_OFF);
	}

	public void SendAPHDGHoldEvent(bool value) {
		TransmitEvent(value ? SimEvent.AP_HDG_HOLD_ON : SimEvent.AP_HDG_HOLD_OFF);
	}
}
