using Sandbox;
using static RunnerVision.PawnController;

namespace RunnerVision;

public partial class Pawn : Component
{
	public Vector3 InputDirection { get; set; }

	public Angles ViewAngles { get; set; }

	public float CameraTiltDeadzone => 10f;

	public float CameraTiltMax => 10f;

	public float CameraTiltMultiplier => 5f;

	public float CameraTilt { get; set; }
	public Angles CameraNewAngles { get; set; }

	private Angles PreviousViewAngles { get; set; }

	[Property]
	public SkinnedModelRenderer Renderer => Components.Get<SkinnedModelRenderer>();

	[Property]
	public Rigidbody Rigidbody => Components.Get<Rigidbody>();

	[Property]
	public GameObject CameraHelper { get; set; }

	[Property]
	public CameraComponent Camera { get; set; }

	public GameObject GroundEntity { get; set; }

	public Vector3 Velocity
	{
		get => Rigidbody.Velocity;
		set => Rigidbody.Velocity = value;
	}

	private Rotation cameraStartRotation { get; set; }
	private Vector3 CurrentCameraOffset { get; set; }

	public BBox Hull
	{
		get => GetHull();
	}

	public BBox GetHull()
	{
		if (Controller.IsDucking())
		{
			return GetDuckingHull();
		}

		return GetStandingHull();
	}

	private BBox GetDuckingHull()
	{
		return new BBox(new Vector3(-16, -16, 0), new Vector3(16, 16, 32));
	}

	private BBox GetStandingHull()
	{
		return new BBox(new Vector3(-16, -16, 0), new Vector3(16, 16, 64));
	}

	public PawnController Controller => Components.Get<PawnController>();

	bool IsThirdPerson { get; set; } = false;

	/// <summary>
	/// Called when the entity is first created
	/// </summary>
	/*
	public override void Spawn()
	{
	    SetModel("models/faith_v2.vmdl");

	    EnableDrawing = true;
	    EnableHideInFirstPerson = false;
	    EnableShadowInFirstPerson = true;

	    CameraHelper = new AnimatedEntity();
	    CameraHelper.Position = Position + Model.GetBoneTransform("CameraJoint").Position;
	    CameraHelper.SetParent(this, "CameraJoint");

	    PostProcessing = Camera.Main.FindOrCreateHook<CameraPostProcessing>();

	    EnableShadowCasting = false;

	    ShadowModel = new("models/faith_shadow.vmdl");
	    ShadowModel.SetParent(this, true);
	    ShadowModel.EnableShadowOnly = true;
	    ShadowModel.EnableShadowCasting = true;
	}
	*/

	// public override void Simulate(IClient cl)
	// {
	// 	UpdateAnimParameters();
	// 	SimulateRotation();
	// 	Controller?.Simulate(cl);
	// 	Animator?.Simulate();

	// 	UpdatePostProcessing();

	// 	TimeSinceSnap += Time.Delta;
	// }

	protected override void OnUpdate()
	{
		if (IsProxy)
			return;

		BuildInput();

		SimulateRotation();

		CameraUpdateRotation();
		CameraUpdateFOV();
		CameraUpdateTilt();

		if (Input.Pressed("view"))
		{
			ToggleThirdPerson();
		}

		if (IsThirdPerson)
		{
			UpdateCameraThirdPerson();
		}
		else
		{
			UpdateCameraFirstPerson();
		}

		UpdateAnimParameters();
		// SimulateRotation();
		Controller?.Simulate();
		// Animator?.Simulate();
	}

	void UpdateAnimParameters()
	{
		Renderer.Set("speed", Velocity.Length);
		Renderer.Set("horizontal_speed", Velocity.WithZ(0).Length);
		Renderer.Set("airborne", !Controller.Grounded);
		Renderer.Set("jumping", Controller.Jumping);
		Renderer.Set("dashing", Controller.Dashing);
		Renderer.Set("wallrunning", (int)Controller.Wallrunning);
		Renderer.Set("vaulting", (int)Controller.Vaulting);
		Renderer.Set("climbing", Controller.Climbing);
		Renderer.Set("ducking", Controller.Ducking);
		Renderer.Set("sliding", Controller.Sliding);
	}

	void BuildInput()
	{
		InputDirection = Input.AnalogMove;

		if (Input.Suppressed)
			return;

		var look = Input.AnalogLook;

		if (ViewAngles.pitch > 90f || ViewAngles.pitch < -90f)
		{
			look = look.WithYaw(look.yaw * -1f);
		}

		var viewAngles = ViewAngles;
		viewAngles += look;
		viewAngles.pitch = viewAngles.pitch.Clamp(-89f, 89f);
		viewAngles.roll = 0f;
		ViewAngles = viewAngles.Normal;
	}

	private void ToggleThirdPerson()
	{
		IsThirdPerson = !IsThirdPerson;
	}

	private void UpdateCameraThirdPerson()
	{
		Vector3 targetPos;
		var pos = WorldPosition + Vector3.Up * 64;
		var rot = Camera.WorldRotation * Rotation.FromAxis(Vector3.Up, -16);

		float distance = 80.0f * WorldScale.z;
		targetPos = pos + rot.Right * ((32 + 50) * WorldScale);
		targetPos += rot.Forward * -distance;

		var tr = Scene
			.Trace.Ray(pos, targetPos)
			.WithAnyTags("solid")
			.IgnoreGameObjectHierarchy(GameObject)
			.Radius(8)
			.Run();

		Camera.WorldPosition = tr.EndPosition;
	}

	private void UpdateCameraFirstPerson()
	{
		bool turningLeft =
			ViewAngles.yaw.NormalizeDegrees() > PreviousViewAngles.yaw.NormalizeDegrees();
		float turnRate = PreviousViewAngles.ToRotation().Distance(ViewAngles.ToRotation());

		if (turnRate > CameraTiltDeadzone)
			CameraTilt = CameraTilt.LerpTo(
				turningLeft ? -CameraTiltMax : CameraTiltMax,
				Time.Delta * CameraTiltMultiplier
			);

		PreviousViewAngles = PreviousViewAngles.LerpTo(ViewAngles, Time.Delta * 50f);

		Camera.WorldRotation = Rotation.From(
			ViewAngles.pitch,
			ViewAngles.yaw,
			ViewAngles.roll + CameraTilt
		);

		UpdateCameraOffset();
		Camera.WorldPosition = WorldPosition + CurrentCameraOffset;

		if (Controller.TimeSinceSnap < 0.5f)
		{
			CameraRotateToNewPosition(15f);
		}
		else
		{
			if (Controller.Climbing)
			{
				LookTowardsWall();
			}

			if (Controller.IsWallRunning())
			{
				LookTowardsWallrunMovement();
			}

			if (Controller.Vaulting == VaultType.OntoHigh)
			{
				LookTowardsVaultTarget();
			}
		}
	}

	private void UpdateCameraOffset()
	{
		var lerpSpeed = GetCameraOffsetLerpSpeed();

		var cameraHelperLocalPosition = CameraHelper.WorldPosition - WorldPosition;
		CurrentCameraOffset = CurrentCameraOffset.LerpTo(
			cameraHelperLocalPosition,
			lerpSpeed * Time.Delta
		);
	}

	private float GetCameraOffsetLerpSpeed()
	{
		if (Controller.IsWallRunning())
			return 30f;

		if (Controller.TimeSinceSlideStopped < 0.5f)
			return 30f;

		return 10f;
	}

	public void LookTowardsSnap()
	{
		if (Controller.IsWallRunning())
		{
			CameraNewAngles = Controller.CurrentWall.Normal.EulerAngles;
		}
		else if (Controller.Climbing)
		{
			CameraNewAngles = Controller.CurrentWall.Normal.EulerAngles.WithPitch(-15f);
		}
		else
		{
			CameraNewAngles = (ViewAngles.Forward * -1f).EulerAngles.WithPitch(0);
		}
	}

	private void LookTowardsWall()
	{
		CameraNewAngles = (Controller.CurrentWall.Normal * -1f).EulerAngles.WithPitch(-40f);
		CameraRotateToNewPosition(speed: 5f);
	}

	private void LookTowardsVaultTarget()
	{
		CameraNewAngles = (
			Controller.VaultTargetPos - Controller.GameObject.WorldPosition
		).EulerAngles.WithPitch(0);
		CameraRotateToNewPosition(speed: 5f);
	}

	private void LookTowardsWallrunMovement()
	{
		if (!Controller.CurrentWall.Hit)
			return;

		if (Controller.TimeSinceWallrun > 0.25f)
			return;

		if (Controller.TimeSinceWallrun < 0.05f)
			return;

		var wallNormalRotation = Controller.CurrentWall.Normal.EulerAngles.ToRotation();

		switch (Controller.Wallrunning)
		{
			case WallRunSide.Left:
				CameraNewAngles = wallNormalRotation.Left.EulerAngles;
				break;
			case WallRunSide.Right:
				CameraNewAngles = wallNormalRotation.Right.EulerAngles;
				break;
			default:
				return;
		}

		CameraRotateToNewPosition(speed: 15f);
	}

	private void CameraUpdateTilt()
	{
		if (Controller.Wallrunning != 0)
		{
			CameraTilt = CameraTilt.LerpTo(
				Controller.Wallrunning == WallRunSide.Left ? 10f : -10f,
				Time.Delta * CameraTiltMultiplier
			);
			return;
		}

		if (Controller.TimeSinceDash < 0.1f)
		{
			CameraTilt = CameraTilt.LerpTo(
				Controller.Dashing == 1 ? -10f : 10f,
				Time.Delta * CameraTiltMultiplier
			);
			return;
		}

		CameraTilt = CameraTilt.LerpTo(0, Time.Delta * CameraTiltMultiplier * 1.1f);
	}

	private void CameraUpdateRotation()
	{
		CameraRotateToViewAngles();
	}

	private void CameraRotateToViewAngles()
	{
		Camera.WorldRotation = ViewAngles.ToRotation();
	}

	private void CameraRotateToNewPosition(float speed = 5f)
	{
		ViewAngles = ViewAngles.LerpTo(CameraNewAngles, speed * Time.Delta);
	}

	private void CameraUpdateFOV()
	{
		Camera.FieldOfView = Screen.CreateVerticalFieldOfView(Preferences.FieldOfView);
	}

	public SceneTraceResult TraceBBox(Vector3 start, Vector3 end, float liftFeet = 0.0f)
	{
		return TraceBBox(start, end, Hull.Mins, Hull.Maxs, liftFeet);
	}

	public SceneTraceResult TraceBBox(
		Vector3 start,
		Vector3 end,
		Vector3 mins,
		Vector3 maxs,
		float liftFeet = 0.0f
	)
	{
		if (liftFeet > 0)
		{
			start += Vector3.Up * liftFeet;
			maxs = maxs.WithZ(maxs.z - liftFeet);
		}

		var tr = Scene
			.Trace.Ray(start, end)
			.Size(mins, maxs)
			.WithAnyTags("solid", "playerclip", "passbullets")
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();

		return tr;
	}

	protected void SimulateRotation()
	{
		// EyeRotation = ViewAngles.ToRotation();
		WorldRotation = ViewAngles.WithPitch(0f).ToRotation();
	}
}
