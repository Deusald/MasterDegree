using UnityEngine;
using DVector2 = DeusaldSharp.Vector2;

namespace MasterDegree
{
    public class PlayerAnimator : MonoBehaviour
    {
        #region Variables

        [SerializeField] private MeshRenderer _MeshRenderer;
        [SerializeField] private Transform    _Transform;

        private readonly Color32[] _PlayersColors =
        {
            new Color32(255, 57, 45, 255), new Color32(85, 189, 255, 255),
            new Color32(255, 218, 74, 255), new Color32(131, 255, 58, 255)
        };

        #endregion Variables

        #region Public Methods

        public void SetColor(byte playerId)
        {
            Material     newMaterial  = new Material(_MeshRenderer.material);
            newMaterial.color      = _PlayersColors[playerId];
            _MeshRenderer.material = newMaterial;
        }

        public void SetDirection(DVector2 direction)
        {
            if (direction == DVector2.Down)
            {
                _Transform.localRotation = Quaternion.Euler(0, -90, 0);
            }
            else if (direction == DVector2.Up)
            {
                _Transform.localRotation = Quaternion.Euler(0, 90, 0);
            }
            else if (direction == DVector2.Left)
            {
                _Transform.localRotation = Quaternion.Euler(0, 0, 0);
            }
            else if (direction == DVector2.Right)
            {
                _Transform.localRotation = Quaternion.Euler(0, 180, 0);
            }
        }

        #endregion Public Methods
    }
}