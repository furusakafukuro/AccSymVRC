using UnityEngine;

public abstract class BaseElectroMagneticFieldCube : MonoBehaviour
{
    // 電磁場を計算する抽象メソッド
    // position: 球の現在位置
    public abstract (Vector3 electricField, Vector3 magneticField) GetElectroMagneticField(Vector3 position);
}