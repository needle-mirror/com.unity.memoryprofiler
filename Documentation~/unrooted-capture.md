# Capture a snapshot with call stacks

Follow the steps on this page to capture a snapshot with call stacks from the Unity Editor and a Player build. To learn more about capturing snapshots, refer to [Snapshots introduction](snapshots-concepts.md).

## Requirements to investigate a snapshot with call stacks

To run the Unity Editor with the `-enable-memoryprofiler-callstacks` command-line argument, your project must use Unity 6000.3.0a1 or newer.

To review a snapshot, your project must contain the [Memory Profiler 1.1.9 package](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest) or newer.

> [!NOTE] Native call stack information requires symbols to be available when you capture a snapshot. Symbols might not be supported on some platforms, such as consoles and Web.

## Capture a snapshot with call stacks in the Editor

To capture a snapshot from the Editor with detailed call stacks:

1. Run the Unity Editor from a command line. To learn how to do this, refer to [Run Unity from a command-line interface](xref:um-command-line-run-unity).
2. Pass the `-enable-memoryprofiler-callstacks` command-line argument.
3. Enter Play mode.
4. Open **Window** > **Analysis** > **Memory Profiler**.
5. Set the capture target to `Editor`.
6. Select **Capture**.

## Capture a snapshot with call stacks in the Player

To capture a snapshot from a Player with detailed call stacks:

1. Open **Build Profiles** > **Platform Settings** and enable **Development build**.
2. Open **Edit** > **Project Settings** > **Player** and set **Scripting Backend** to `IL2CPP`.
3. Open **Edit** > **Project Settings** > **Player** and set **IL2CPP Stacktrace Information** to `Method Name, File Name, and Line Number`.
4. Start a build.
5. Run the Player build from a command line. To learn how to do this, refer to [Run Unity from a command-line interface](xref:um-command-line-run-unity).
6. Pass the `-enable-memoryprofiler-callstacks` command-line argument.
7. Open **Window** > **Analysis** > **Memory Profiler**.
8. Set the capture target to `Player`.
9. Select **Capture**.

## Investigate a snapshot

To review the recorded call stacks:

1. Open **Window** > **Analysis** > **Memory Profiler**.
2. Select the **All Of Memory** tab.
3. Navigate to **Native** > **Unity Subsystems** > **Unrooted** > **Unrooted**.
4. Select allocations under the **Unrooted** category.
5. In the right-hand panel, find the **Call Stack Info** section.
6. Review call stacks.

> [!NOTE] To open the right-hand panel, select **Show/Hide the details panel**.

## Additional resources

- [Snapshots introduction](snapshots-concepts.md)
- [Snapshots panel reference](snapshots-component.md)
- [Inspect memory usage](inspect-memory-usage.md)
- [Memory usage on devices](memory-on-device.md)
