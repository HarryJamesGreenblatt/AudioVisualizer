namespace AudioVisualizer.Engine;

/// <summary>
/// Component responsible for advancing an entity's physical state (velocity, gravity, collision).
/// Called once per fixed-timestep tick.
/// </summary>
public interface IPhysicsComponent
{
    /// <summary>
    /// Advance physics by one fixed-timestep tick.
    /// </summary>
    /// <param name="entity">The owning entity whose position/velocity may be mutated.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    void Update(SceneEntity entity, float dt);
}
