using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// SimulationController_Udon
/// - Replaces BeamParticle.cs + ElectronBeamEmitter.cs with a single controller.
/// - Uses EMFieldCube_Udon (uniform E/B inside cube region) for field sampling.
/// - Integrator: Relativistic Boris method (Boris push) using u = γ v.
/// Units:
/// - Physics position: meters (m)
/// - Physics u: m/s (since u = γ v)
/// - World/visual position: Unity units. If you want EMFieldCube_Udon regions defined in meters,
///   keep metersToUnity = 1 so 1 Unity unit = 1 m (recommended).
/// </summary>
public class SimulationController_Udon_xySpread : UdonSharpBehaviour
{
    [Header("Pool (pre-placed particle objects)")]
    [Tooltip("Particle GameObjects are children of this transform. They will be activated/deactivated as a pool.")]
    public Transform poolRoot;

    [Header("Field Cubes (EMFieldCube_Udon)")]
    [Tooltip("List of field cubes. Fields are summed for cubes that contain the particle position.")]
    public EMFieldCube_Udon[] fieldCubes;

    [Header("Emission (poolRoot local coordinates, meters)")]
    [Tooltip("Emission position in meters, expressed in poolRoot local coordinates.")]
    public Vector3 nozzlePosition_m = new Vector3(0f, 0f, 0f);

    [Tooltip("Emission direction in poolRoot local coordinates (will be normalized).")]
    public Vector3 nozzleDirection = new Vector3(0f, 0f, 1f);

    [Tooltip("Emission rate [particles/second].")]
    public float emitRate = 2000f;

    [Tooltip("Initial speed [m/s].")]
    public float v0_mps = 3.0e7f;

    [Tooltip("Particle lifetime [s].")]
    public float lifeTime_s = 0.3f;

    [Header("Angular spread (RMS)")] 
    [Tooltip("RMS divergence angle in X (mrad). 0 disables X spread.")]
    public float sigmaThetaX_mrad = 0f;

    [Tooltip("RMS divergence angle in Y (mrad). 0 disables Y spread.")]
    public float sigmaThetaY_mrad = 0f;


    [Header("Units / Visual")]
    [Tooltip("Meters to Unity units. Recommended = 1 when using EMFieldCube_Udon regions defined in meters.")]
    public float metersToUnity = 1.0f;

    [Header("Particle Properties (SI)")]
    [Tooltip("Particle mass [kg]. Electron: 9.10938356e-31, Proton: 1.6726219e-27")]
    public float particleMass_kg = 9.10938356e-31f;

    [Tooltip("Particle charge [C]. Electron: -1.602176634e-19, Proton: +1.602176634e-19")]
    public float particleCharge_C = -1.602176634e-19f;

    [Tooltip("Speed of light [m/s].")]
    public float speedOfLight_mps = 299792458f;

    [Header("Time Stepping")]
    public bool paused = false;

    [Tooltip("Base time step [s]. 0 => Time.fixedDeltaTime.")]
    public float fixedDt_s = 0.0f;

    [Tooltip("Max sub-steps per FixedUpdate (safety).")]
    public int maxSubSteps = 8;

    [Header("Performance / Safety")]
    [Tooltip("Max particles to emit per frame (safety).")]
    public int maxEmitPerFrame = 100;

    [Header("Optional Billboard")]
    public bool billboardToLocalPlayer = true;

    // Pool objects
    private GameObject[] _objs;
    private int _n;

    // Physics state arrays
    private bool[] _alive;
    private float[] _age;
    private float[] _life;
    private Vector3[] _pos_m;   // position in meters (poolRoot local)
    private Vector3[] _u_mps;   // u = gamma*v in m/s (poolRoot local)

    private float _emitAcc = 0f;
    private int _nextIndex = 0;

    private VRCPlayerApi _localPlayer;

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;

        BuildPool();

        // Normalize nozzle direction
        if (nozzleDirection.sqrMagnitude < 1e-12f) nozzleDirection = Vector3.forward;
        else nozzleDirection = nozzleDirection.normalized;

        // Safety warning: EMFieldCube_Udon assumes 1 Unity unit == 1 meter for cubeSize_m.
        if (Mathf.Abs(metersToUnity - 1f) > 1e-3f)
        {
            Debug.LogWarning("[SimulationController_Udon_xySpread] metersToUnity != 1. EMFieldCube_Udon cubeSize_m is in meters but containment uses Unity coordinates. Prefer metersToUnity=1, or scale your whole scene consistently.");
        }
    }

    private void BuildPool()
    {
        if (poolRoot == null)
        {
            _n = 0;
            _objs = new GameObject[0];
            _alive = new bool[0];
            _age = new float[0];
            _life = new float[0];
            _pos_m = new Vector3[0];
            _u_mps = new Vector3[0];
            return;
        }

        _n = poolRoot.childCount;
        _objs = new GameObject[_n];

        _alive = new bool[_n];
        _age = new float[_n];
        _life = new float[_n];
        _pos_m = new Vector3[_n];
        _u_mps = new Vector3[_n];

        for (int i = 0; i < _n; i++)
        {
            Transform c = poolRoot.GetChild(i);
            _objs[i] = c != null ? c.gameObject : null;

            _alive[i] = false;
            _age[i] = 0f;
            _life[i] = 0f;
            _pos_m[i] = Vector3.zero;
            _u_mps[i] = Vector3.zero;

            if (_objs[i] != null) _objs[i].SetActive(false);
        }
    }

    private void FixedUpdate()
    {
        if (paused) return;
        if (_n <= 0) return;

        float dtFrame = Time.fixedDeltaTime;
        float dtBase = fixedDt_s > 0f ? fixedDt_s : dtFrame;
        dtBase = Mathf.Max(1e-6f, dtBase);

        int steps = Mathf.CeilToInt(dtFrame / dtBase);
        steps = Mathf.Clamp(steps, 1, Mathf.Max(1, maxSubSteps));
        float dt = dtFrame / steps;

        for (int s = 0; s < steps; s++)
        {
            StepOnce(dt);
        }
    }

    private void StepOnce(float dt)
    {
        // a) emit
        float rate = Mathf.Max(0f, emitRate);
        _emitAcc += rate * dt;

        int emitN = Mathf.FloorToInt(_emitAcc);
        if (emitN > 0) _emitAcc -= emitN;
        emitN = Mathf.Clamp(emitN, 0, Mathf.Max(0, maxEmitPerFrame));

        for (int k = 0; k < emitN; k++)
        {
            SpawnOne();
        }

        // b) integrate particles
        for (int i = 0; i < _n; i++)
        {
            if (!_alive[i]) continue;

            _age[i] += dt;
            if (_age[i] >= _life[i])
            {
                Kill(i);
                continue;
            }

            // Sample field in WORLD coordinates (Unity units).
            // Convert particle local position (meters) -> world position (Unity units)
            Vector3 worldPosU = poolRoot.TransformPoint(_pos_m[i] * metersToUnity);

            Vector3 E_Vm = Vector3.zero;
            Vector3 B_T = Vector3.zero;
            SampleField(worldPosU, ref E_Vm, ref B_T);

            // Integrate u and position in poolRoot local (meters)
            Vector3 u = _u_mps[i];
            BorisStep(ref u, ref _pos_m[i], E_Vm, B_T, dt);

            _u_mps[i] = u;

            // Apply transform
            if (_objs[i] != null)
            {
                _objs[i].transform.localPosition = _pos_m[i] * metersToUnity;
                if (billboardToLocalPlayer) Billboard(_objs[i].transform);
            }
        }
    }

    private void SampleField(Vector3 worldPosU, ref Vector3 E_Vm, ref Vector3 B_T)
    {
        if (fieldCubes == null) return;

        int m = fieldCubes.Length;
        for (int j = 0; j < m; j++)
        {
            EMFieldCube_Udon cube = fieldCubes[j];
            if (cube == null) continue;

            Vector3 e, b;
            if (cube.GetUniformEB(worldPosU, out e, out b))
            {
                E_Vm += e;
                B_T += b;
            }
        }
    }

    private void SpawnOne()
    {
        int idx = FindDeadIndex();
        if (idx < 0) return;

        // Initial position (meters, poolRoot local)
        _pos_m[idx] = nozzlePosition_m;

        // Initial u = gamma*v in m/s
        float v0 = Mathf.Max(0f, v0_mps);
        Vector3 dir = MakeInitialDirection();
        Vector3 v = dir * v0;

        float c = Mathf.Max(1f, speedOfLight_mps);
        float v2 = v.sqrMagnitude;
        if (v2 >= (0.999999f * c) * (0.999999f * c))
        {
            // clamp to avoid gamma blow-up
            float vmag = Mathf.Sqrt(v2);
            v = v * ((0.999999f * c) / Mathf.Max(1e-9f, vmag));
            v2 = v.sqrMagnitude;
        }
        float gamma = 1.0f / Mathf.Sqrt(1.0f - (v2 / (c * c)));
        _u_mps[idx] = v * gamma;

        _age[idx] = 0f;
        _life[idx] = Mathf.Max(0.01f, lifeTime_s);
        _alive[idx] = true;

        if (_objs[idx] != null)
        {
            _objs[idx].SetActive(true);
            _objs[idx].transform.localPosition = _pos_m[idx] * metersToUnity;
            if (billboardToLocalPlayer) Billboard(_objs[idx].transform);
        }
    }

    private int FindDeadIndex()
    {
        if (_n <= 0) return -1;

        for (int step = 0; step < _n; step++)
        {
            int i = (_nextIndex + step) % _n;
            if (!_alive[i] && _objs[i] != null)
            {
                _nextIndex = (i + 1) % _n;
                return i;
            }
        }
        return -1;
    }


private Vector3 MakeInitialDirection()
{
    Vector3 f = nozzleDirection;
    float f2 = f.sqrMagnitude;
    if (f2 < 1e-12f) f = Vector3.forward;
    else f = f / Mathf.Sqrt(f2);

    float sx = Mathf.Max(0f, sigmaThetaX_mrad) * 1e-3f; // mrad -> rad
    float sy = Mathf.Max(0f, sigmaThetaY_mrad) * 1e-3f;

    if (sx <= 0f && sy <= 0f) return f;

    // Build an orthonormal basis (right, up, forward=f)
    Vector3 upRef = Vector3.up;
    float dot = Mathf.Abs(Vector3.Dot(upRef, f));
    if (dot > 0.99f) upRef = Vector3.right;

    Vector3 r = Vector3.Cross(upRef, f);
    float r2 = r.sqrMagnitude;
    if (r2 < 1e-12f) r = Vector3.right;
    else r = r / Mathf.Sqrt(r2);

    Vector3 u = Vector3.Cross(f, r); // already normalized

    // Gaussian (normal) angles: theta_x ~ N(0, sx), theta_y ~ N(0, sy)
    float thx = (sx > 0f) ? (RandNormal() * sx) : 0f;
    float thy = (sy > 0f) ? (RandNormal() * sy) : 0f;

    Vector3 d = f + r * thx + u * thy;
    float d2 = d.sqrMagnitude;
    if (d2 < 1e-12f) return f;
    return d / Mathf.Sqrt(d2);
}

// Box-Muller transform using UnityEngine.Random.value (UdonSharp-friendly)
private float RandNormal()
{
    float u1 = Mathf.Max(1e-7f, Random.value);
    float u2 = Random.value;
    float mag = Mathf.Sqrt(-2.0f * Mathf.Log(u1));
    float z0 = mag * Mathf.Cos(2.0f * Mathf.PI * u2);
    return z0;
}

    private void Kill(int i)
    {
        _alive[i] = false;
        _age[i] = 0f;
        _life[i] = 0f;
        _u_mps[i] = Vector3.zero;

        if (_objs[i] != null) _objs[i].SetActive(false);
    }

    /// <summary>
    /// Relativistic Boris push for u = gamma*v (m/s).
    /// E in V/m, B in Tesla, dt in seconds.
    /// Updates u and position x (meters, poolRoot local).
    /// </summary>
    private void BorisStep(ref Vector3 u, ref Vector3 x_m, Vector3 E_Vm, Vector3 B_T, float dt)
    {
        float q = particleCharge_C;
        float m = Mathf.Max(1e-30f, particleMass_kg);
        float c = Mathf.Max(1f, speedOfLight_mps);

        // a) half electric impulse
        float qm = q / m;
        Vector3 uMinus = u + (qm * (dt * 0.5f)) * E_Vm;

        // b) rotation
        float u2 = uMinus.sqrMagnitude;
        float gamma = Mathf.Sqrt(1.0f + (u2 / (c * c)));

        Vector3 t = (qm * (dt * 0.5f) / gamma) * B_T;
        float t2 = t.sqrMagnitude;
        Vector3 s = (2.0f / (1.0f + t2)) * t;

        Vector3 uPrime = uMinus + Vector3.Cross(uMinus, t);
        Vector3 uPlus = uMinus + Vector3.Cross(uPrime, s);

        // c) half electric impulse
        Vector3 uNew = uPlus + (qm * (dt * 0.5f)) * E_Vm;
        u = uNew;

        // d) advance position with v = u/gamma
        float uNew2 = uNew.sqrMagnitude;
        float gammaNew = Mathf.Sqrt(1.0f + (uNew2 / (c * c)));
        Vector3 v = uNew / gammaNew;

        x_m += v * dt;
    }

    private void Billboard(Transform tr)
    {
        if (tr == null) return;
        if (_localPlayer == null) return;

        Vector3 headPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        Vector3 dir = headPos - tr.position;
        if (dir.sqrMagnitude < 1e-8f) return;

        // face the camera
        tr.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}
