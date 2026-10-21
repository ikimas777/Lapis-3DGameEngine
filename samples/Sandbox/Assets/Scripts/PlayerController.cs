/// <summary>Moves the entity with W A S D or the arrow keys.</summary>
public class PlayerController : Behaviour
{
    [Range(0, 20)]
    [Tooltip("Movement speed in units per second.")]
    public float speed = 5.0f;

    public override void Update()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // -Z is forward in Lapis, so W (Vertical = +1) moves along -Z.
        // ClampLength keeps diagonal movement as fast as straight movement.
        Vector3 direction = Vector3.ClampLength(new Vector3(x, 0, -z), 1f);

        transform.Position += direction * speed * Time.DeltaTime;
    }
}
