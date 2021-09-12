using GameLogicCommon;
using UnityEngine;

namespace MasterDegree
{
    public class BonusMaterial : MonoBehaviour
    {
        #region Variables

        [SerializeField] private MeshRenderer _MeshRenderer;
        [SerializeField] private Texture      _BombBonusTexture;
        [SerializeField] private Texture      _FireBonusTexture;
        [SerializeField] private Texture      _DetonatorBonusTexture;

        #endregion Variables

        #region Public Methods

        public void SetBonusTextureOnMaterial(Game.BonusType bonusType)
        {
            Material     newMaterial  = new Material(_MeshRenderer.material);

            switch (bonusType)
            {
                case Game.BonusType.Power:
                {
                    newMaterial.mainTexture = _FireBonusTexture;
                    break;
                }
                case Game.BonusType.Bomb:
                {
                    newMaterial.mainTexture = _BombBonusTexture;
                    break;
                }
                case Game.BonusType.Detonator:
                {
                    newMaterial.mainTexture = _DetonatorBonusTexture;
                    break;
                }
            }

            _MeshRenderer.material = newMaterial;
        }

        #endregion Public Methods
    }
}