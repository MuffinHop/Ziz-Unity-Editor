# Shape Markers Setup Guide

## Quick Setup to See Shape Markers

### Step 1: Create the Shapes System
1. Create an empty GameObject in your scene
2. Add the `Shapes` component to it
3. In the Inspector, check these settings:
   - **Use Children As Shapes**: ✓ (checked)
   - **Auto Draw From Children**: ✓ (checked)

### Step 2: Test with the Test Script
1. Create another empty GameObject
2. Add the `ShapeMarkersTest` component to it
3. In the Inspector:
   - **Create Test Shapes**: ✓ (checked) 
   - **Manual Draw**: ✗ (unchecked) - try this first
4. Press Play and check the Console for debug messages

### Step 3: If shapes don't appear, try Manual Draw
1. Stop the scene
2. Check **Manual Draw**: ✓ (checked)
3. Press Play again - you should see basic shapes

## Troubleshooting

### No shapes visible at all:
- Check if there's a `Shapes` component in the scene
- Verify the camera is positioned to see the shapes (they draw at world positions)
- Check Console for error messages

### Shapes appear with Manual Draw but not with Shape Markers:
- Verify `UseChildrenAsShapes` and `AutoDrawFromChildren` are enabled
- Check that child GameObjects have the correct ShapeMarker components
- Look at Console debug messages showing what components were found

### Shape Markers have wrong colors/sizes:
- Check the properties on each ShapeMarker component
- Colors might be very dark (try bright colors like Red, Blue, Yellow)
- Sizes might be too small (try radius 1.0 instead of 0.5)

## Manual Shape Marker Creation

To manually create shape markers:

1. Create a GameObject
2. Add one of these components:
   - `CircleMarker` 
   - `BoxMarker`
   - `TriangleMarker` 
   - `StarMarker`
   - `HexagonMarker`
   - `LineMarker` (needs a target transform)
3. Set the color and size properties
4. Make sure the GameObject is active and has a Shapes parent or that Shapes.Instance exists

## Code Example

```csharp
// Enable hierarchy drawing
Shapes.Instance.UseChildrenAsShapes = true;
Shapes.Instance.AutoDrawFromChildren = true;

// Draw shapes
Shapes.Instance.Begin();
Shapes.Instance.ForceDrawFromChildren(); // or automatic via End()
Shapes.Instance.End();
```
