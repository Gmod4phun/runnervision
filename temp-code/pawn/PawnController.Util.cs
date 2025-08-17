using System;
using Sandbox;

namespace RunnerVision;

public partial class PawnController
{
	Rotation GetVelocityRotation()
	{
		return Pawn.Velocity.EulerAngles.ToRotation();
	}

	bool AngleWithinRange(
		Vector3 directionVector1,
		Vector3 directionVector2,
		float minAngle = 0f,
		float maxAngle = 360f
	)
	{
		if (directionVector1.Normal.Angle(directionVector2) > maxAngle)
			return false;

		if (directionVector1.Normal.Angle(directionVector2) < minAngle)
			return false;

		return true;
	}

	Vector3 GetMoveVector()
	{
		var movement = Pawn.InputDirection.Normal;
		var angles = Pawn.ViewAngles.WithPitch(0);
		return Rotation.From(angles) * movement * CurrentMaxSpeed;
	}

	void UpdateDash()
	{
		if (TimeSinceDash > 0.5f)
			Dashing = 0;
	}

	void InitiateJumpOffWall()
	{
		var cameraDirection = GetCameraDirection();
		var forwardAngle = GetForwardAngle();

		var forwardMultiplier = Math.Max(0.5f, forwardAngle / 90f);

		var jumpVector = cameraDirection * 300f * forwardMultiplier + Pawn.WorldRotation.Up * 300f;

		// Pawn.Velocity *= 0.5f;
		// Pawn.Rigidbody.ApplyImpulse(jumpVector);

		Pawn.Velocity = Pawn.Velocity * 0.5f;
		Pawn.Velocity += jumpVector;

		previousWallrunNormal = CurrentWall.Normal;
		Wallrunning = 0;

		Jumping = true;
	}

	void InitiateJumpOffWallSnapTurned()
	{
		var forwardAngle = GetCameraDirection();
		var jumpVector = forwardAngle * 150f + Pawn.WorldRotation.Up * 300f;

		Pawn.Velocity = Pawn.Velocity * 0.5f;
		Pawn.Velocity += jumpVector;

		Jumping = true;
	}

	Vector3 GetCameraDirection()
	{
		return Pawn.Camera.WorldRotation.Forward.WithZ(0);
	}

	float GetForwardAngle()
	{
		var cameraDirection = GetCameraDirection();
		var forwardAngle = ForwardDirection.Angle(cameraDirection);

		// Check angle from movement axis (max 90 degrees)
		float dotProduct = Vector3.Dot(ForwardDirection, cameraDirection);

		if (dotProduct < 0)
		{
			return 180 - forwardAngle;
		}

		return forwardAngle;
	}

	void InitiateDash()
	{
		if (TimeSinceDash > 1.0f)
		{
			var isLeft = Input.Down("left");

			Dashing = isLeft ? 1 : 2;

			var impulse = (isLeft ? Pawn.WorldRotation.Left : Pawn.WorldRotation.Right) * 300f;

			// Pawn.Rigidbody.ApplyImpulse(
			// 	(isLeft ? Pawn.WorldRotation.Left : Pawn.WorldRotation.Right) * 300f
			// );

			Pawn.Velocity += impulse;

			CurrentMaxSpeed += 200f;

			TimeSinceDash = 0.0f;
		}
	}

	void DisableParkourLock()
	{
		parkouredSinceJumping = false;
		wallrunSinceJumping = false;
	}

	void DoFall()
	{
		Pawn.Velocity += Vector3.Down * (IsWallRunning() ? Gravity * 0.60f : Gravity) * Time.Delta;
	}

	void InitiateLandingOnFloor()
	{
		Sound.Play("concretefootstepland", Pawn.WorldPosition + Vector3.Down * 10f);
		AddEvent("grounded");

		Wallrunning = 0;
		previousWallrunNormal = Vector3.Zero;

		parkouredSinceJumping = false;
		parkouredBeforeLanding = false;

		Jumping = false;

		if (Pawn.Velocity.WithZ(0).Length > 100f)
		{
			CurrentMaxSpeed += 200;
		}
	}

	void UpdateMaxSpeed(Vector3 moveVector)
	{
		if (moveVector.LengthSquared != 0)
		{
			CurrentMaxSpeed = CurrentMaxSpeed.Approach(
				MaxSpeed,
				Time.Delta * 50f * SpeedGrowthRate
			);
		}
		else
		{
			CurrentMaxSpeed = CurrentMaxSpeed.Approach(
				StartingSpeed,
				Time.Delta * 50f * SpeedShrinkRate
			);
		}
	}

	void ClampMaxSpeed()
	{
		CurrentMaxSpeed = Math.Min(CurrentMaxSpeed, MaxSpeed);
	}

	bool IsDashing()
	{
		return Dashing != 0;
	}

	bool ShouldDash()
	{
		if (Ducking)
			return false;

		if (!Grounded)
			return false;

		if (Input.Down("forward"))
			return false;

		if (!Input.Down("left") && !Input.Down("right"))
			return false;

		return true;
	}

	void HandleNoclipping()
	{
		var movement = Pawn.InputDirection.Normal;
		var angles = Pawn.ViewAngles;
		var moveVector = Rotation.From(angles) * movement * 10f;

		Pawn.WorldTransform = Pawn.WorldTransform.Add(moveVector, true);
		Pawn.Velocity = 0;
	}

	void IncreaseDeltaTime()
	{
		TimeSinceLastFootstep += Time.Delta;
		TimeSinceLastFootstepRelease += Time.Delta;
		TimeSinceDash += Time.Delta;
		TimeSinceClimbing += Time.Delta;
		TimeSinceWallrun += Time.Delta;
		TimeSinceSlideStopped += Time.Delta;
		TimeSinceSnap += Time.Delta;
	}

	void UpdateFootsteps()
	{
		float speed = Pawn.Velocity.Length;

		// if (Game.IsServer)
		// 	return;

		if (speed == 0f)
			return;

		if (!Grounded && !IsWallRunning() && !Climbing)
			return;

		float nextStep = 70f / speed;
		String footstepSound = speed < 300 ? "concretefootstepwalk" : "concretefootsteprun";
		String footstepReleaseSound = IsWallRunning()
			? "concretefootstepwallrunrelease"
			: "concretefootsteprunrelease";

		if (IsWallRunning())
		{
			nextStep = 60f / speed;
			footstepSound = "concretefootstepwallrun";
		}

		if (Climbing)
		{
			nextStep = 0.2f;
			footstepSound = "concretefootstepwallrun";
		}

		if (TimeSinceLastFootstep > nextStep)
		{
			Sound.Play(footstepSound, Pawn.WorldPosition + Vector3.Down * 10f);

			TimeSinceLastFootstep = 0f;
		}

		if (TimeSinceLastFootstepRelease > nextStep * 1.15 && speed > StartFootSoundVelocity)
		{
			Sound.Play(footstepReleaseSound, Pawn.WorldPosition + Vector3.Down * 10f);

			TimeSinceLastFootstepRelease = 0f;
		}
	}

	void AdjustSharpTurn(Vector3 moveVector)
	{
		if (Pawn.Velocity.Angle(moveVector.Normal * moveVector.Length) > SharpTurnAngle)
		{
			CurrentMaxSpeed = CurrentMaxSpeed.Approach(
				StartingSpeed,
				Time.Delta * 50f * SpeedShrinkRate
			);
		}
	}

	void InitiateJump()
	{
		Jumping = true;
		Pawn.Velocity = ApplyJump(Pawn.Velocity, "jump");
	}

	bool CanJump()
	{
		if (!Grounded)
			return false;

		if (IsVaulting())
			return false;

		if (IsDashing())
			return false;

		if (IsWallRunning())
			return false;

		if (IsDucking())
			return false;

		return true;
	}

	GameObject CheckForGround()
	{
		if (Pawn.Velocity.z > 100f)
			return null;

		var trace = Pawn.TraceBBox(Pawn.WorldPosition, Pawn.WorldPosition + Vector3.Down, 2f);

		if (!trace.Hit)
			return null;

		if (trace.Normal.Angle(Vector3.Up) > GroundAngle)
			return null;

		return trace.GameObject;
	}

	Vector3 ApplyFriction(Vector3 velocity, float frictionAmount)
	{
		float StopSpeed = 100.0f;

		var speed = velocity.Length;
		if (speed < 0.1f)
			return velocity;

		// Bleed off some speed, but if we have less than the bleed
		// threshold, bleed the threshold amount.
		float control = (speed < StopSpeed) ? StopSpeed : speed;

		// Add the amount to the drop amount.
		var drop = control * Time.Delta * frictionAmount;

		// scale the velocity
		float newspeed = speed - drop;
		if (newspeed < 0)
			newspeed = 0;
		if (newspeed == speed)
			return velocity;

		newspeed /= speed;
		velocity *= newspeed;

		return velocity;
	}

	void DoMovement(Vector3 moveVector)
	{
		if (ShouldAccelerate())
			DoAccelerate(moveVector);

		DoApplyFriction();
	}

	void DoAccelerate(Vector3 moveVector)
	{
		Pawn.Velocity = Accelerate(
			Pawn.Velocity,
			moveVector.Normal,
			moveVector.Length,
			CurrentMaxSpeed,
			Acceleration
		);
	}

	void DoApplyFriction()
	{
		Pawn.Velocity = ApplyFriction(Pawn.Velocity, GetFriction());
	}

	bool ShouldAccelerate()
	{
		if (IsSliding())
			return false;

		return true;
	}

	float GetFriction()
	{
		if (IsSliding())
			return Friction * 0.5f;

		return Friction;
	}

	Vector3 Accelerate(
		Vector3 velocity,
		Vector3 wishdir,
		float wishspeed,
		float speedLimit,
		float acceleration
	)
	{
		if (speedLimit > 0 && wishspeed > speedLimit)
			wishspeed = speedLimit;

		velocity = velocity.LerpTo(wishdir * wishspeed, Time.Delta * 45f * acceleration);

		return velocity;
	}

	Vector3 ApplyJump(Vector3 velocity, string jumpType)
	{
		AddEvent(jumpType);
		return velocity.WithZ(0) + Vector3.Up * JumpSpeed;
	}

	void UpdateMoveHelper(GameObject groundEntity)
	{
		var mh = new CharacterControllerHelper(Scene.Trace, Pawn.WorldPosition, Pawn.Velocity);
		mh.Trace = mh.Trace.Size(Pawn.Hull).IgnoreGameObjectHierarchy(Pawn.GameObject);

		if (mh.TryMoveWithStep(Time.Delta, StepSize) > 0)
		{
			if (Grounded)
			{
				mh.Position = StayOnGround(mh.Position);
			}
			Pawn.WorldPosition = mh.Position;
			Pawn.Velocity = mh.Velocity;
		}

		Pawn.GroundEntity = groundEntity;
	}

	Vector3 StayOnGround(Vector3 position)
	{
		var start = position + Vector3.Up * 2;
		var end = position + Vector3.Down * StepSize;

		// See how far up we can go without getting stuck
		var trace = Pawn.TraceBBox(position, start);
		start = trace.EndPosition;

		// Now trace down from a known safe position
		trace = Pawn.TraceBBox(start, end);

		if (trace.Fraction <= 0)
			return position;
		if (trace.Fraction >= 1)
			return position;
		if (trace.StartedSolid)
			return position;
		if (Vector3.GetAngle(Vector3.Up, trace.Normal) > GroundAngle)
			return position;

		return trace.EndPosition;
	}

	float GetSpeed()
	{
		return Pawn.Velocity.Length;
	}

	float GetHorizontalSpeed()
	{
		return Pawn.Velocity.WithZ(0).Length;
	}
}
