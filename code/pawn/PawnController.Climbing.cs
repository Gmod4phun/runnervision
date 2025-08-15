using System;
using Sandbox;

namespace RunnerVision;

public partial class PawnController
{
	private Vector3 climbTargetXY = Vector3.Zero;

	void UpdateClimbing()
	{
		if (IsWallRunning() || IsVaulting() || Grounded)
		{
			TimeSinceClimbing = 0f;
			CurrentClimbAmount = 0;
			StopClimbing();
			return;
		}

		var traceFront = Scene
			.Trace.Ray(
				from: Pawn.WorldPosition
					+ Pawn.WorldRotation.Up * 50f
					+ Pawn.WorldRotation.Forward * 15f,
				to: Pawn.WorldPosition
					+ Pawn.WorldRotation.Up * 50f
					+ Pawn.WorldRotation.Forward * 35f
			)
			.Run();

		if (debugMode)
			DebugOverlay.Line(
				from: Pawn.WorldPosition
					+ Pawn.WorldRotation.Up * 50f
					+ Pawn.WorldRotation.Forward * 15f,
				to: Pawn.WorldPosition
					+ Pawn.WorldRotation.Up * 50f
					+ Pawn.WorldRotation.Forward * 35f
			);

		if (!ShouldClimb(traceFront))
		{
			StopClimbing();
			return;
		}

		if (ShouldInitiateClimb(traceFront))
			InitiateClimbing(traceFront);

		if (IsClimbing())
		{
			DoClimbing();
		}
	}

	bool ShouldInitiateClimb(SceneTraceResult traceFront)
	{
		return !IsClimbing() && CanClimb(traceFront);
	}

	bool CanClimb(SceneTraceResult traceFront)
	{
		var cameraDirection = GetCameraDirection();

		// Don't climb when facing wall at a wide angle
		if (!AngleWithinRange(cameraDirection, traceFront.Normal, minAngle: 150f))
			return false;

		BBox boxInfrontOfWall = GetBoxInfrontOfWall(traceFront);

		if (debugMode)
			DebugOverlay.Box(box: boxInfrontOfWall, color: Color.Orange, duration: showDebugTime);

		SceneTraceResult traceBoxInfrontOfWall = Scene
			.Trace.Box(bbox: boxInfrontOfWall, from: 0, to: 0)
			.Run();

		if (traceBoxInfrontOfWall.Hit)
			return false;

		return true;
	}

	BBox GetBoxInfrontOfWall(SceneTraceResult traceFront)
	{
		return new BBox(
			mins: Vector3.Forward * +boxRadius + Vector3.Up * 45f + Vector3.Left * boxRadius,
			maxs: Vector3.Forward * -boxRadius + Vector3.Up * 120f + Vector3.Right * boxRadius
		).Translate(
			traceFront.HitPosition + Vector3.Down * 80f + traceFront.Normal * (boxRadius + 10f)
		);
	}

	bool ShouldClimb(SceneTraceResult traceFront)
	{
		if (!Input.Down("jump"))
			return false;

		if (Pawn.Velocity.z < 0)
			return false;

		if (CurrentClimbAmount >= MaxClimbAmount)
			return false;

		if (!traceFront.Hit)
			return false;

		if (IsDucking())
			return false;

		return true;
	}

	void InitiateClimbing(SceneTraceResult traceFront)
	{
		Climbing = true;
		CurrentWall = traceFront;

		Pawn.Camera.WorldRotation = new Rotation(traceFront.Normal, 10f);
		climbTargetXY =
			traceFront.HitPosition + Pawn.WorldRotation.Down * 50f + traceFront.Normal * 30f;
	}

	void ApproachClimbTarget()
	{
		Vector3 newPos = Pawn.WorldPosition.WithZ(0).LerpTo(climbTargetXY, Time.Delta * 10f);

		Pawn.WorldPosition = new Vector3(newPos.x, newPos.y, Pawn.WorldPosition.z);
	}

	void StopClimbing()
	{
		Climbing = false;
	}

	void DoClimbing()
	{
		Pawn.Velocity = Pawn.Velocity.LerpTo(Pawn.Velocity.WithX(0).WithY(0), 10f * Time.Delta);

		if (TimeSinceClimbing > 0.15f)
		{
			var addHorizontalSpeed = Math.Min(50f, Pawn.Velocity.WithZ(0).Length);
			Pawn.Rigidbody.ApplyImpulse(
				Pawn.WorldRotation.Up * 100f + Pawn.WorldRotation.Up * addHorizontalSpeed
			);
			TimeSinceClimbing = 0f;
			CurrentClimbAmount++;
		}

		ApproachClimbTarget();
	}

	bool IsClimbing()
	{
		return Climbing;
	}
}
