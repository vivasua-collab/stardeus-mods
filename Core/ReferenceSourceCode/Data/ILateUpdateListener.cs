namespace Game.Data {
    // Basically MonoBehaviour.Update();
    public interface ILateUpdateListener {
        void OnLateUpdate(); 
    }
}
