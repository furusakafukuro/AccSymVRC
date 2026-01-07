using UnityEngine;

public class UniformMagneticFieldCube : BaseElectroMagneticFieldCube
{
    public Vector3 uniformFieldStrength = Vector3.up; // 一定磁場の方向と強さ
    public Vector3 uniformElectricField = Vector3.zero; // 必要に応じて一定電場を設定可能

    public override (Vector3 electricField, Vector3 magneticField) GetElectroMagneticField(Vector3 position)
    {
        Bounds bounds = GetComponent<Collider>().bounds;

        // Cubeの外では磁場と電場は0
        if (!bounds.Contains(position))
            return (Vector3.zero, Vector3.zero);

        return (uniformElectricField, uniformFieldStrength);
    }
}