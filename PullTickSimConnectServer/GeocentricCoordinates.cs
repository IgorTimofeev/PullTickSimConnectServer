using System.Numerics;

namespace PullTickSimConnectServer;

public class GeocentricCoordinates(float latitude, float longitude, float altitude) {
	public GeocentricCoordinates() : this(0, 0, 0) {

	}

	public const float EquatorialRadius = 6378137;

	public float Latitude { get; set; } = latitude;
	public float Longitude { get; set; } = longitude;
	public float Altitude { get; set; } = altitude;

	public Vector3 ToCartesian() {
		var radius = EquatorialRadius + Altitude;
		var latCos = MathF.Cos(Latitude);

		return new(
			radius * latCos * MathF.Cos(Longitude),
			radius * latCos * MathF.Sin(Longitude),
			radius * MathF.Sin(Latitude)
		);
	}
}
