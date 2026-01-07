using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ElectronBeamEmitter : UdonSharpBehaviour
{
    [Header("Pool (pre-placed objects)")]
    [Tooltip("BeamParticle が付いた粒子オブジェクトを子に持つ親 Transform")]
    public Transform poolRoot;

    [Header("Emission")]
    [Tooltip("放出位置（poolRoot のローカル座標系）")]
    public Vector3 nozzlePositionU = new Vector3(0f, 0f, 0f);

    [Tooltip("放出方向（poolRoot のローカル座標系）。正規化して使う")]
    public Vector3 nozzleDirectionU = new Vector3(0f, 0f, 1f);

    [Tooltip("放出レート [particles/second]")]
    public float emitRate = 2000f;

    [Tooltip("初期速度 [m/s]（物理単位）")]
    public float v0_ms = 3.0e7f;

    [Tooltip("粒子寿命 [s]")]
    public float lifeTime_s = 0.3f;

    [Header("Unit Scale")]
    [Tooltip("1 meter を Unity 座標で何倍にするか。例：0.001 なら 1m=0.001U")]
    public float metersToUnity = 0.001f;

    [Header("Simulation")]
    public bool paused = false;

    [Tooltip("Unity の FixedUpdate 1回で進める最大ステップ数（負荷保護）")]
    public int maxSubSteps = 8;

    [Tooltip("固定刻み [s]。0なら Time.fixedDeltaTime を使う")]
    public float fixedDt_s = 0.0f;

    [Header("Optional: Simple Constant Acceleration (debug)")]
    [Tooltip("デバッグ用：一定加速度を与える（Unity単位/s^2）")]
    public bool useConstantAccel = false;

    public Vector3 constantAccelU = new Vector3(0f, 0f, 0f);

    // Pool
    private BeamParticle[] particles;
    private int poolCount;

    // Emission accumulator
    private float emitAcc;

    private void Start()
    {
        BuildPool();
    }

    private void BuildPool()
    {
        if (poolRoot == null)
        {
            particles = new BeamParticle[0];
            poolCount = 0;
            return;
        }

        int n = poolRoot.childCount;
        particles = new BeamParticle[n];
        poolCount = n;

        for (int i = 0; i < n; i++)
        {
            Transform c = poolRoot.GetChild(i);
            BeamParticle bp = c.GetComponent<BeamParticle>();
            particles[i] = bp;
            if (bp != null) bp.Kill();
        }
    }

    private void FixedUpdate()
    {
        if (paused) return;
        if (particles == null || poolCount == 0) return;

    UnityEngine.Debug.Log("FixedUpdate running");
    
        float dtFrame = Time.fixedDeltaTime;
        float dtBase = (fixedDt_s > 0f) ? fixedDt_s : dtFrame;

        // 1フレームで複数サブステップ（dtが大きい時に安定化）
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
        emitAcc += rate * dt;

        int emitN = Mathf.FloorToInt(emitAcc);
        if (emitN > 0) emitAcc -= emitN;

        for (int k = 0; k < emitN; k++)
        {
            SpawnOne();
        }

        // b) update particles
        for (int i = 0; i < poolCount; i++)
        {
            BeamParticle p = particles[i];
            if (p == null || !p.Alive) continue;

            if (useConstantAccel)
            {
                p.StepWithAcceleration(dt, constantAccelU);
            }
            else
            {
                p.StepKinematic(dt);
            }
        }
    }

    private void SpawnOne()
    {
        BeamParticle p = FindDeadParticle();
        if (p == null) return;

        Vector3 dirU = nozzleDirectionU;
        float dirMag = dirU.magnitude;
        if (dirMag < 1e-9f) dirU = Vector3.forward;
        else dirU /= dirMag;

        // 物理速度[m/s] -> Unity速度[U/s]
        float v0U = v0_ms * metersToUnity;
        Vector3 velU = dirU * v0U;

        p.Init(nozzlePositionU, velU, lifeTime_s);
    }

    private BeamParticle FindDeadParticle()
    {
        // 単純線形検索（小〜中規模プール向け）
        for (int i = 0; i < poolCount; i++)
        {
            BeamParticle p = particles[i];
            if (p == null) continue;
            if (!p.Alive) return p;
        }
        return null;
    }
}