using Combat.ECS;
using Unity.Entities;
using UnityEngine;

namespace Combat.Authoring
{
    /// <summary>
    /// Bakes a 4×4 damage multiplier matrix (AmmoType × ArmorType) as a singleton buffer.
    /// Place on a single GameObject inside the SubScene.
    ///
    /// Table layout: [ammoType * 4 + armorType] = multiplier.
    /// </summary>
    public sealed class DamageMatrixAuthoring : MonoBehaviour
    {
        [Header("SmallArms vs ...")] [SerializeField]
        private float smallArms_Unarmored = 1.0f;

        [SerializeField] private float smallArms_Light = 0.3f;
        [SerializeField] private float smallArms_Medium = 0.05f;
        [SerializeField] private float smallArms_Heavy = 0.0f;

        [Header("AP vs ...")] [SerializeField] private float ap_Unarmored = 0.8f;
        [SerializeField] private float ap_Light = 1.0f;
        [SerializeField] private float ap_Medium = 0.8f;
        [SerializeField] private float ap_Heavy = 0.6f;

        [Header("HE vs ...")] [SerializeField] private float he_Unarmored = 1.5f;
        [SerializeField] private float he_Light = 0.6f;
        [SerializeField] private float he_Medium = 0.2f;
        [SerializeField] private float he_Heavy = 0.1f;

        [Header("HEAT vs ...")] [SerializeField]
        private float heat_Unarmored = 1.2f;

        [SerializeField] private float heat_Light = 1.0f;
        [SerializeField] private float heat_Medium = 1.0f;
        [SerializeField] private float heat_Heavy = 0.8f;

        private sealed class Baker : Baker<DamageMatrixAuthoring>
        {
            public override void Bake(DamageMatrixAuthoring a)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent<DamageMatrixTag>(entity);

                var buffer = AddBuffer<DamageMatrixEntry>(entity);
                buffer.Length = 16;

                // SmallArms (0)
                buffer[0 * 4 + 0] = new DamageMatrixEntry { Multiplier = a.smallArms_Unarmored };
                buffer[0 * 4 + 1] = new DamageMatrixEntry { Multiplier = a.smallArms_Light };
                buffer[0 * 4 + 2] = new DamageMatrixEntry { Multiplier = a.smallArms_Medium };
                buffer[0 * 4 + 3] = new DamageMatrixEntry { Multiplier = a.smallArms_Heavy };

                // AP (1)
                buffer[1 * 4 + 0] = new DamageMatrixEntry { Multiplier = a.ap_Unarmored };
                buffer[1 * 4 + 1] = new DamageMatrixEntry { Multiplier = a.ap_Light };
                buffer[1 * 4 + 2] = new DamageMatrixEntry { Multiplier = a.ap_Medium };
                buffer[1 * 4 + 3] = new DamageMatrixEntry { Multiplier = a.ap_Heavy };

                // HE (2)
                buffer[2 * 4 + 0] = new DamageMatrixEntry { Multiplier = a.he_Unarmored };
                buffer[2 * 4 + 1] = new DamageMatrixEntry { Multiplier = a.he_Light };
                buffer[2 * 4 + 2] = new DamageMatrixEntry { Multiplier = a.he_Medium };
                buffer[2 * 4 + 3] = new DamageMatrixEntry { Multiplier = a.he_Heavy };

                // HEAT (3)
                buffer[3 * 4 + 0] = new DamageMatrixEntry { Multiplier = a.heat_Unarmored };
                buffer[3 * 4 + 1] = new DamageMatrixEntry { Multiplier = a.heat_Light };
                buffer[3 * 4 + 2] = new DamageMatrixEntry { Multiplier = a.heat_Medium };
                buffer[3 * 4 + 3] = new DamageMatrixEntry { Multiplier = a.heat_Heavy };
            }
        }
    }
}