using System;
using System.Collections.Generic;
using Sandbox;

namespace RunnerVision;

public partial class PawnController : Component
{
	[Property]
	public Pawn Pawn => Components.Get<Pawn>();

	public bool Noclipping { get; set; }

	public int StepSize => 26;
	public int GroundAngle => 50;
	public int JumpSpeed => 300;
	public float Gravity => 800f;
	public float StartingSpeed => 1000f;
	public float MaxSpeed => 1500f;
	public float SpeedGrowthRate => 2.0f;
	public float SpeedShrinkRate => 50.0f;
	public float Friction => 3.0f;
	public float SharpTurnAngle => 50f;
	public float Acceleration => 0.02f;
	public float StartFootSoundVelocity => 300f;
	public int MaxClimbAmount => 4;

	[Property]
	public bool Climbing { get; set; }

	[Property]
	public WallRunSide Wallrunning { get; set; }

	[Property]
	public int Dashing { get; set; }

	[Property]
	public VaultType Vaulting { get; set; }

	public float TimeSinceDash { get; set; }
	public Vector3 ForwardDirection { get; set; }

	[Property]
	public float TimeSinceClimbing { get; set; }
	public float TimeSinceWallrun { get; set; }
	public float TimeSinceSlideStopped { get; set; }

	[Property]
	public float TimeSinceClimbingStopped { get; set; }

	public SceneTraceResult CurrentWall { get; set; } = new SceneTraceResult();

	[Property]
	public bool Jumping { get; set; }
	public Vector3 VaultTargetPos { get; set; }

	[Property]
	public bool Ducking { get; set; }

	[Property]
	public bool Sliding { get; set; }

	private int CurrentClimbAmount { get; set; }
	public float CurrentMaxSpeed { get; set; }
	private float TimeSinceLastFootstep { get; set; }
	private float TimeSinceLastFootstepRelease { get; set; }

	public float TimeSinceSnap { get; set; }
	private Vector3 VaultStartPos { get; set; }

	private float bezierCounter = 0f;
	private float vaultSpeed = 0f;

	[Property]
	public bool debugMode { get; set; }
	private bool parkouredSinceJumping = false;
	private bool wallrunSinceJumping = false;
	private Vector3 previousWallrunNormal = Vector3.Zero;
	private bool parkouredBeforeLanding = false;

	HashSet<string> ControllerEvents = new(StringComparer.OrdinalIgnoreCase);

	public bool Grounded => Pawn.GroundEntity.IsValid();

	public PawnController()
	{
		CurrentMaxSpeed = StartingSpeed;
	}

	public void Simulate()
	{
		ControllerEvents.Clear();

		// DebugOverlay.ScreenText("Climbing: " + IsClimbing().ToString(), line: 5);
		// DebugOverlay.ScreenText("Wallrunning: " + Wallrunning.ToString(), line: 6);
		// DebugOverlay.ScreenText("Vaulting: " + Vaulting.ToString(), line: 7);
		// DebugOverlay.ScreenText("Grounded: " + Grounded.ToString(), line: 8);
		// DebugOverlay.ScreenText("Ducking: " + Ducking.ToString(), line: 9);
		// DebugOverlay.ScreenText("Sliding: " + Sliding.ToString(), line: 10);
		// DebugOverlay.ScreenText(
		// 	"Current Speed: " + ((int)Entity.Velocity.Length).ToString(),
		// 	line: 11
		// );
		// DebugOverlay.ScreenText("Current Accel: " + CurrentMaxSpeed.ToString(), line: 12);
		// DebugOverlay.ScreenText("Max Accel: " + MaxSpeed.ToString(), line: 13);

		if (Input.Pressed("score"))
		{
			Noclipping = !Noclipping;
		}

		if (Noclipping)
		{
			// TODO: don't return here
			HandleNoclipping();
			return;
		}

		if (IsVaulting())
		{
			// TODO: don't return here
			ProgressVault();
			return;
		}

		UpdateWallrunning();

		var groundEntity = CheckForGround();
		var moveVector = GetMoveVector();

		UpdateMaxSpeed(moveVector);
		AdjustSharpTurn(moveVector);

		if (groundEntity.IsValid())
		{
			if (!Grounded)
			{
				InitiateLandingOnFloor();
			}
		}

		UpdateMoveHelper(groundEntity);

		if (Grounded)
		{
			DoMovement(moveVector);
		}
		else
		{
			DoFall();
		}

		if (Input.Released("jump"))
		{
			DisableParkourLock();
		}

		if (Input.Pressed("jump"))
		{
			if (ShouldDash())
			{
				InitiateDash();
			}
		}

		if (Input.Pressed("jump"))
		{
			if (Jumping)
				return;

			if (IsWallRunning())
			{
				InitiateJumpOffWall();
			}
			else if (IsSnapTurningAfterClimb())
			{
				InitiateJumpOffWallSnapTurned();
			}

			if (CanJump())
			{
				InitiateJump();
			}
		}

		if (Input.Down("jump") && !parkouredSinceJumping)
		{
			bool successfulWallrun = TryWallrunning();

			if (!successfulWallrun)
				InitiateVault();
		}

		if (Input.Down("duck"))
		{
			TryDucking();
		}
		else
		{
			if (IsDucking() && !IsSliding())
				StopDucking();
		}

		if (Input.Pressed("Snap Turn 180 degrees"))
		{
			if (TimeSinceSnap < 0.5f)
				return;

			Pawn.LookTowardsSnap();
			TimeSinceSnap = 0f;
		}

		if (Input.Down("walk"))
		{
			CurrentMaxSpeed = 400;
		}

		if (Input.Released("walk"))
		{
			CurrentMaxSpeed = 1000;
		}

		UpdateDash();
		UpdateDuck();
		UpdateSlide();

		if (debugMode)
		{
			// DebugOverlay.ScreenText(CurrentMaxSpeed.ToString());
			// DebugOverlay.ScreenText(Entity.Position.ToString(), 3);

			// DebugOverlay.ScreenText(Vaulting.ToString(), 1);
		}

		UpdateFootsteps();
		IncreaseDeltaTime();
		UpdateClimbing();

		ClampMaxSpeed();

		ForwardDirection = GetVelocityRotation().Forward.WithZ(0);
	}

	public bool HasEvent(string eventName)
	{
		return ControllerEvents.Contains(eventName);
	}

	void AddEvent(string eventName)
	{
		if (HasEvent(eventName))
			return;

		ControllerEvents.Add(eventName);
	}
}
