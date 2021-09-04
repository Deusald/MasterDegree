using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MasterDegree
{
    public class InfoSystem : MonoBehaviour
    {
        #region Variables

        [SerializeField] private CanvasGroup     _CanvasGroup;
        [SerializeField] private TextMeshProUGUI _Text;

        private Queue<(string, float)> _Infos;

        private const float _AnimationSpeed = 4f;

        #endregion Variables

        #region Special Methods

        private void Awake()
        {
            _Infos = new Queue<(string, float)>();
            StartCoroutine(AnimationLoop());
        }

        #endregion Special Methods
        
        #region Public Methods

        public void AddInfo(string infoText, float screenTime)
        {
            _Infos.Enqueue((infoText, screenTime));
        }

        #endregion Public Methods

        #region Private Methods

        private IEnumerator AnimationLoop()
        {
            while (true)
            {
                if (_Infos.Count == 0)
                {
                    yield return new WaitForEndOfFrame();
                    continue;
                }
                
                (string infoText, float screenTime) = _Infos.Dequeue();

                _Text.text = infoText;

                while (_CanvasGroup.alpha < 1)
                {
                    _CanvasGroup.alpha += Time.deltaTime * _AnimationSpeed;
                    yield return new WaitForEndOfFrame();
                }

                yield return new WaitForSeconds(screenTime);
                
                while (_CanvasGroup.alpha > 0)
                {
                    _CanvasGroup.alpha -= Time.deltaTime * _AnimationSpeed;
                    yield return new WaitForEndOfFrame();
                }
            }
        }

        #endregion Private Methods
    }
}