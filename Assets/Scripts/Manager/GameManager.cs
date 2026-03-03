using MoreMountains.Tools;
using UnityEngine;

namespace Manager
{
    public class GameManager : MMSingleton<MonoBehaviour>
    {
        #region Properties

        public PlayerState.PlayerStates CurrentPlayerState { get; private set; }
        private PlayerState.PlayerStates _currentPlayerState;
        
        #endregion
        
        #region Methods
        public void ChangePlayerState(PlayerState.PlayerStates newState)
        {
            _currentPlayerState = CurrentPlayerState;
            CurrentPlayerState = newState;
            Debug.Log($"Player state changed to: {CurrentPlayerState}");
        }
        #endregion
    }
}
