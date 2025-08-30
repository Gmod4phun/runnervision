using Sandbox;

public class ParkourPlayer : Component, Component.ExecuteInEditor
{
	[Property]
	public SkinnedModelRenderer Lower { get; set; }

	[Property]
	public SkinnedModelRenderer Upper { get; set; }

	[Property]
	public CameraComponent Camera { get; set; }

	[Property]
	public Vector2 ViewAngles { get; set; }

	Transform cameraDesiredTransform;

	[Property]
	public bool HandsFollowCameraVertical { get; set; }

	[Property, Range(0, 1), Step(0.01f)]
	public float FollowCameraBlend { get; set; }

	public bool Grounded => Controller.Grounded;

	public bool Jumping => Controller.Jumping;

	public ParkourController Controller => Components.Get<ParkourController>();

	public Vector3 InputDirection { get; set; }

	public BBox Hull
	{
		get => /*Controller.Ducking ? GetDuckingHull() : */
			GetStandingHull();
	}

	private BBox GetDuckingHull()
	{
		return new BBox(new Vector3(-16, -16, 0), new Vector3(16, 16, 32));
	}

	private BBox GetStandingHull()
	{
		return new BBox(new Vector3(-16, -16, 0), new Vector3(16, 16, 64));
	}

	void AdjustCameraBlendVales()
	{
		FollowCameraBlend = FollowCameraBlend.LerpTo(
			HandsFollowCameraVertical ? 1 : 0,
			Time.Delta * (HandsFollowCameraVertical ? 3 : 9)
		);
	}

	void HandleUpperBodyTransform()
	{
		Upper.LocalRotation = Rotation.FromAxis(Vector3.Up, -ViewAngles.x);
	}

	void HandleLowerBodyTransform()
	{
		Lower.LocalPosition += Lower.RootMotion.Position;
		Lower.LocalRotation *= Lower.RootMotion.Rotation;

		var yawDiff = Rotation.Difference(Upper.LocalRotation, Lower.LocalRotation).Yaw();

		var canRepositionLegs = Grounded;

		if (yawDiff > 45 || yawDiff < -45)
		{
			// rotate body to match arms
			Lower.Set("body_turning_dir", yawDiff < 0 ? 0 : 1);

			if (yawDiff >= 90 || yawDiff <= -90)
			{
				if (canRepositionLegs)
				{
					Lower.Set("body_turning_90", true);
					Lower.Set("body_turning_45", false);
				}

				// Log.Info("turn 90");

				Lower.LocalRotation =
					Upper.LocalRotation * Rotation.FromYaw(yawDiff < 0 ? -90 : 90);
			}
			else if (yawDiff < 80 && yawDiff > -80)
			{
				if (canRepositionLegs)
				{
					Lower.Set("body_turning_90", false);
					Lower.Set("body_turning_45", true);
				}

				// Log.Info("turn 45");
			}
		}
	}

	void BuildInput()
	{
		var look = Input.AnalogLook;

		var viewAngles = ViewAngles;
		viewAngles.x -= look.yaw;
		viewAngles.y -= look.pitch;
		viewAngles.x = viewAngles.x.UnsignedMod(360f);
		viewAngles.y = viewAngles.y.Clamp(-90f, 90);
		ViewAngles = viewAngles;

		InputDirection = Input.AnalogMove;
	}

	// swan neck forward, swan neck down
	(float, float) GetSwanNeckValues()
	{
		if (ViewAngles.y < 0)
		{
			var percentSwanNeck = ViewAngles.y / -90f;
			return (percentSwanNeck * 8, percentSwanNeck * 8);
		}

		return (0f, 0f);
	}

	public SceneTraceResult TraceBBox(Vector3 start, Vector3 end, float liftFeet = 0.0f)
	{
		return TraceBBox(start, end, Hull.Mins, Hull.Maxs, liftFeet);
	}

	private SceneTraceResult TraceBBox(
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

	void CameraTransformLogic()
	{
		if (!Camera.IsValid())
			return;

		Upper.TryGetBoneTransform("EyeJoint", out var camBoneTx);
		camBoneTx.Rotation *= Rotation.From(-90, -90, 0);
		cameraDesiredTransform = camBoneTx;
		cameraDesiredTransform.Rotation *= Rotation.FromPitch(-ViewAngles.y);

		var (swanForward, swanDown) = GetSwanNeckValues();

		cameraDesiredTransform.Position += cameraDesiredTransform.Up * swanForward;
		cameraDesiredTransform.Position += cameraDesiredTransform.Forward * swanDown;

		var upperSpineBone = Upper.Model.Bones.GetBone("SpineX");
		if (Upper.GetBoneObject(upperSpineBone) is GameObject spineBoneObject)
		{
			Upper.TryGetBoneTransformAnimation(upperSpineBone, out var origBoneTx);

			var followCameraTx = origBoneTx;

			followCameraTx.Rotation *= Rotation.FromAxis(-Vector3.Forward, ViewAngles.y);
			followCameraTx.Position += followCameraTx.Up * swanDown;
			followCameraTx.Position += -followCameraTx.Right * swanForward;

			Transform finalTransform;

			finalTransform.Position = Vector3.Lerp(
				origBoneTx.Position,
				followCameraTx.Position,
				FollowCameraBlend
			);

			finalTransform.Rotation = Rotation.Slerp(
				origBoneTx.Rotation,
				followCameraTx.Rotation,
				FollowCameraBlend
			);

			finalTransform.Scale = 1;

			spineBoneObject.WorldTransform = finalTransform;
		}

		Camera.GameObject.WorldTransform = cameraDesiredTransform;
	}

	public Vector3 GetMoveVector()
	{
		return Rotation.From(0, -ViewAngles.x + WorldRotation.Yaw(), 0) * InputDirection.Normal;
	}

	void HandleAnimgraphParameters()
	{
		Lower.Set("jumping", Jumping);
		Upper.Set("jumping", Jumping);
	}

	protected override void OnUpdate()
	{
		if (!Lower.IsValid() || !Upper.IsValid() || !Camera.IsValid())
			return;

		BuildInput();
		AdjustCameraBlendVales();

		HandleUpperBodyTransform();
		HandleLowerBodyTransform();

		HandleAnimgraphParameters();

		CameraTransformLogic();
	}
}
