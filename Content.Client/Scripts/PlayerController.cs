using Godot;

public partial class PlayerController : CharacterBody3D
{
	[Export]
	public float MoveSpeed { get; set; } = 5.0f;

	[Export]
	public float StopDistance { get; set; } = 0.1f;

	private Vector3 _targetPosition;
	private bool _hasTarget = false;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent)
			return;

		if (mouseEvent.ButtonIndex != MouseButton.Left ||
			!mouseEvent.Pressed)
			return;

		Camera3D camera = GetViewport().GetCamera3D();

		if (camera == null)
			return;

		Vector3 rayOrigin =
			camera.ProjectRayOrigin(mouseEvent.Position);

		Vector3 rayDirection =
			camera.ProjectRayNormal(mouseEvent.Position);

		// Пока считаем, что земля находится на высоте Y = 0.
		if (Mathf.Abs(rayDirection.Y) < 0.0001f)
			return;

		float distance =
			-rayOrigin.Y / rayDirection.Y;

		if (distance <= 0.0f)
			return;

		Vector3 clickedPosition =
			rayOrigin + rayDirection * distance;

		_targetPosition = new Vector3(
			clickedPosition.X,
			GlobalPosition.Y,
			clickedPosition.Z
		);

		_hasTarget = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_hasTarget)
		{
			Velocity = Vector3.Zero;
			return;
		}

		Vector3 direction =
			_targetPosition - GlobalPosition;

		direction.Y = 0.0f;

		if (direction.Length() <= StopDistance)
		{
			Velocity = Vector3.Zero;
			_hasTarget = false;
			return;
		}

		Velocity =
			direction.Normalized() * MoveSpeed;

		MoveAndSlide();
	}
}
