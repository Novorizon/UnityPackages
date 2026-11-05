namespace UI
{
    public enum UILayer
    {
        Background = 0,
        Page = 10,
        Popup = 20,
        Panel = 25, // Serialized identity is stable; UIManager maps ordinary panels below Popup when rendering.
        Toast = 30,
        Overlay = 40
    }
}