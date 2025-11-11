using UnityEngine;

/// <summary>
/// Simple test script to verify Shape animation recording functionality.
/// Attach this to a Shape object and call StartTest() to begin recording.
/// </summary>
public class ShapeAnimationTest : MonoBehaviour
{
    private Shape shape;
    private float testDuration = 2.0f; // 2 seconds of animation
    private float startTime;

    void Start()
    {
        shape = GetComponent<Shape>();
        if (shape == null)
        {
            Debug.LogError("ShapeAnimationTest requires a Shape component!");
            return;
        }
    }

    /// <summary>
    /// Starts the animation recording test
    /// </summary>
    public void StartTest()
    {
        if (shape == null) return;

        Debug.Log("Starting Shape animation recording test...");
        shape.StartAnimationRecording();
        startTime = Time.time;

        // Animate the shape for testing (simple scale oscillation)
        StartCoroutine(AnimateShape());
    }

    private System.Collections.IEnumerator AnimateShape()
    {
        while (Time.time - startTime < testDuration)
        {
            // Simple animation: oscillate scale
            float t = (Time.time - startTime) / testDuration;
            float scale = 1.0f + 0.5f * Mathf.Sin(t * Mathf.PI * 4); // 2 full oscillations
            transform.localScale = Vector3.one * scale;

            yield return null;
        }

        // Stop recording and export
        shape.StopAnimationRecording();
        Debug.Log("Shape animation recording test completed!");
    }

    void OnGUI()
    {
        if (GUILayout.Button("Start Shape Animation Test"))
        {
            StartTest();
        }
    }
}