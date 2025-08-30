using System;
using Sandbox;

namespace Sandbox
{
	public class ParkourController : Component
	{
		private int _stuckTries;

		public float StepHeight => 12f;
		public float GroundAngle => 45f;
		public float Acceleration => 12f;
		public float Gravity => 800f;
		public float Friction => 3;

		[Property, ReadOnly]
		public float MoveLimit = 150f;

		[Property, ReadOnly]
		public float CurrentMaxSpeed = 0;

		public float MaxSpeedRunning { get; set; } = 325;

		[Property, Group("Collision")]
		public TagSet HitLayers { get; set; } = new TagSet();

		[Property, Group("Collision")]
		public TagSet IgnoreLayers { get; set; } = new TagSet();

		[Property, Group("Acceleration")]
		public Curve AccelerationCurve { get; set; } =
			new Curve(
				new Curve.Frame[]
				{
					new(0f, 1f), // At 0% speed, 100% acceleration
					new(0.5f, 0.8f), // At 50% speed, 80% acceleration
					new(0.8f, 0.4f), // At 80% speed, 40% acceleration
					new(1f, 0.1f) // At 100% speed, 10% acceleration
				}
			);

		public Vector3 Velocity { get; set; }

		[Property, ReadOnly]
		public float Speed => Velocity.Length;

		[Property, ReadOnly]
		public float KMH => MathX.InchToMeter(Speed) * 3.6f;

		[Property]
		public bool Grounded { get; set; }
		public GameObject GroundObject { get; set; }
		public Collider GroundCollider { get; set; }

		[Property]
		public bool Jumping { get; set; }

		[Property]
		public ParkourPlayer Player => Components.Get<ParkourPlayer>();

		protected override void DrawGizmos()
		{
			Gizmo.Draw.LineBBox(Player.Hull);
		}

		void DecideCurrentMaxSpeed()
		{
			CurrentMaxSpeed = 500;
		}

		float GetAccelerationMultiplier()
		{
			if (!Grounded && Jumping)
			{
				return 0.1f;
			}

			return 1f;
		}

		public void Accelerate(Vector3 vector)
		{
			var mul = GetAccelerationMultiplier();

			Velocity = Velocity.WithAcceleration(
				vector.Normal * MoveLimit * mul,
				Acceleration * Time.Delta
			);
		}

		public void ApplyFriction(float stopSpeed = 140f)
		{
			if (!ShouldApplyFriction())
				return;

			Velocity = Velocity.WithFriction(GetFriction() * Time.Delta, stopSpeed);
		}

		private SceneTrace BuildTrace(Vector3 from, Vector3 to)
		{
			return BuildTrace(Scene.Trace.Ray(from, to));
		}

		private SceneTrace BuildTrace(SceneTrace source)
		{
			var trace = source.Size(Player.Hull).IgnoreGameObjectHierarchy(GameObject);

			return trace.WithAnyTags(HitLayers).WithoutTags(IgnoreLayers);
		}

		/// <summary>
		/// Trace the controller's current position to the specified delta
		/// </summary>
		// public SceneTraceResult TraceDirection(Vector3 direction)
		// {
		// 	return BuildTrace(GameObject.WorldPosition, GameObject.WorldPosition + direction).Run();
		// }

		private void Move()
		{
			if (Grounded)
			{
				Velocity = Velocity.WithZ(0f);
			}
			else
			{
				Velocity += Vector3.Down * Gravity * Time.Delta;
			}

			if (Velocity.Length < 0.001f)
			{
				Velocity = Vector3.Zero;
				return;
			}

			if (Velocity.WithZ(0).Length > MoveLimit)
			{
				Velocity = Velocity.WithZ(0).Normal * MoveLimit;
			}

			var startPos = GameObject.WorldPosition;
			var helper = new CharacterControllerHelper(
				BuildTrace(startPos, startPos),
				startPos,
				Velocity
			);

			helper.Bounce = 0;
			helper.MaxStandableAngle = GroundAngle;

			if (Grounded)
			{
				helper.TryMoveWithStep(Time.Delta, StepHeight);
			}
			else
			{
				helper.TryMove(Time.Delta);
			}

			WorldPosition = helper.Position;
			Velocity = helper.Velocity;
		}

		private void CategorizePosition()
		{
			var worldPos = WorldPosition;
			var downTrace = worldPos + Vector3.Down * 2f;
			bool wasOnGround = Grounded;

			if (!Grounded && Velocity.z > 40f)
			{
				ClearGround();
				return;
			}

			downTrace.z -= wasOnGround ? StepHeight : 0.1f;
			var traceResult = BuildTrace(worldPos, downTrace).Run();

			if (!traceResult.Hit || Vector3.GetAngle(Vector3.Up, traceResult.Normal) > GroundAngle)
			{
				ClearGround();
				return;
			}

			Grounded = true;
			GroundObject = traceResult.GameObject;
			GroundCollider = traceResult.Shape?.Collider as Collider;

			if (
				wasOnGround
				&& !traceResult.StartedSolid
				&& traceResult.Fraction > 0f
				&& traceResult.Fraction < 1f
			)
			{
				WorldPosition = traceResult.EndPosition;
			}

			if (!wasOnGround)
			{
				DoLand();
			}
		}

		/// <summary>
		/// Disconnect from ground and punch our velocity. This is useful if you want the
		/// player to jump or something.
		/// </summary>
		public void Punch(Vector3 amount)
		{
			ClearGround();
			Velocity += amount;
		}

		private void ClearGround()
		{
			Grounded = false;
			GroundObject = null;
			GroundCollider = null;
		}

		/// <summary>
		/// Move a character, with this velocity
		/// </summary>
		public void DoMove()
		{
			Accelerate(Player.GetMoveVector());
			ApplyFriction();

			if (!TryUnstuck())
			{
				Move();
				CategorizePosition();
			}
		}

		/// <summary>
		/// Move from our current position to this target position, but using tracing and
		/// sliding. This is good for different control modes like ladders and stuff.
		/// </summary>
		public void MoveTo(Vector3 targetPosition, bool useStep)
		{
			if (!TryUnstuck())
			{
				var startPos = WorldPosition;
				var velocity = targetPosition - startPos;
				var helper = new CharacterControllerHelper(
					BuildTrace(startPos, startPos),
					startPos,
					velocity
				);

				helper.MaxStandableAngle = GroundAngle;

				if (useStep)
				{
					helper.TryMoveWithStep(1f, StepHeight);
				}
				else
				{
					helper.TryMove(1f);
				}

				WorldPosition = helper.Position;
			}
		}

		private bool TryUnstuck()
		{
			if (!BuildTrace(WorldPosition, WorldPosition).Run().StartedSolid)
			{
				_stuckTries = 0;
				return false;
			}

			const int maxAttempts = 20;
			for (int i = 0; i < maxAttempts; i++)
			{
				var testPos = WorldPosition + Vector3.Random.Normal * (_stuckTries / 2f);

				if (i == 0)
				{
					testPos = WorldPosition + Vector3.Up * 2f;
				}

				if (!BuildTrace(testPos, testPos).Run().StartedSolid)
				{
					WorldPosition = testPos;
					return false;
				}
			}

			_stuckTries++;
			return true;
		}

		bool ShouldApplyFriction()
		{
			// if (IsSliding())
			// 	return false;

			if (!Grounded)
				return false;

			return true;
		}

		float GetFriction()
		{
			// if (IsSliding())
			// 	return Friction * 0.5f;

			return Friction;
		}

		void UpdateMoveLimit()
		{
			if (MoveLimit < 150)
			{
				MoveLimit = 150;
			}

			var isMoving =
				(Input.Down("forward") || !Grounded)
				&& !Input.Down("backward")
				&& (Velocity.Length > 50);

			if (isMoving)
			{
				// Handle moving logic
				var mult = 0.4f + Math.Abs(MoveLimit / (MaxSpeedRunning - 25) - 1);

				// If close to max speed, reduce multiplier
				if (MoveLimit > (MaxSpeedRunning - 100))
				{
					mult *= 0.35f;
				}

				mult *= mult;

				MoveLimit = Math.Clamp(MoveLimit + mult, 0, MaxSpeedRunning);
			}
			else
			{
				// Handle not moving logic

				MoveLimit = Math.Clamp(MoveLimit - 40, 0, MaxSpeedRunning);
			}
		}

		void DoLand()
		{
			if (Jumping)
			{
				Jumping = false;
				Log.Info("landed from jump");
			}
			else
			{
				Log.Info("landed from fall");
			}
		}

		protected override void OnUpdate()
		{
			if (!Player.IsValid())
				return;

			DecideCurrentMaxSpeed();

			UpdateMoveLimit();

			if (Input.Pressed("jump") && Grounded)
			{
				Jumping = true;
				Punch(Vector3.Up * 350);
			}

			DoMove();
		}
	}
}
