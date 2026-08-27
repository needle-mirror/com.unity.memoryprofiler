namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Precomputed status classification for objects in the snapshot.
    /// Computed during <see cref="RootAndImpactInfo"/> processing and stored per-object.
    /// Mirrors the classification logic of SelectedItemDetailsForTypesAndObjects.UpdateStatusAndHint()
    /// but as an enum for use in table filtering, sorting, and display across all views.
    /// </summary>
    // IMPORTANT: Status values are stored as bitmask bits (bit N = status N) in ObjectStatusWarningInfo
    // and comparison ExtraData. The bitmask is a ulong, so this enum must not exceed 64 values.
    // A runtime guard in ObjectStatusExtensions asserts this if new values are added near the limit.
    internal enum ObjectStatus : byte
    {
        /// <summary>
        /// Default / not yet classified. Should not appear in a fully processed snapshot.
        /// Might get used for group rows, structural nodes, or memory regions that don't correspond to real objects, such as Reserved or Untracked.
        /// </summary>
        None = 0,

        // =====================================================
        // Unity Object statuses
        // (mirrors UpdateStatusAndHint dispatch order)
        // =====================================================

        /// <summary>
        /// A Unity Object whose native side has been destroyed but whose managed shell persists
        /// because it is still referenced by other managed objects. "Fake null" object.
        /// </summary>
        LeakedShell,

        /// <summary>
        /// A leaked managed shell that is held alive specifically by a GCHandle
        /// (including potential Unity Object types detected via m_CachedPtr).
        /// </summary>
        LeakedShellHeldByGCHandle,

        /// <summary>
        /// A persistent scene object that is queued for delayed deletion at the time of capture.
        /// </summary>
        InDelayedDeletionQueue,

        /// <summary>
        /// A native MonoBehaviour or ScriptableObject instance whose backing script (MonoScript) is
        /// missing, so it is unreferenced and unrooted yet not cleaned up automatically. Collected into
        /// the Broken Script References bucket by <see cref="RootAndImpactInfo"/>; effectively a leak.
        /// </summary>
        BrokenScriptReference,

        // =====================================================
        // Dynamic Assets (IsAssetObject && !IsPersistentAsset)
        // Created at runtime via Instantiate/new/duplication
        // =====================================================

        /// <summary>
        /// Dynamically created asset with no references and not marked DontDestroyOnLoad. Leaked.
        /// </summary>
        DynamicAssetLeaked,

        /// <summary>
        /// Dynamically created asset that is still referenced.
        /// </summary>
        DynamicAssetReferenced,

        /// <summary>
        /// Dynamically created asset marked DontDestroyOnLoad (referenced or not).
        /// </summary>
        DynamicAssetDontUnload,

        // =====================================================
        // Persistent Assets (IsPersistentAsset)
        // Loaded from disk or associated with a file
        // =====================================================

        /// <summary>
        /// A persistent asset that has references pointing to it (TotalRefCount > 0).
        /// </summary>
        UsedAsset,

        /// <summary>
        /// A persistent asset with no references (TotalRefCount == 0), not DontDestroyOnLoad.
        /// Waiting to be unloaded by Resources.UnloadUnusedAssets() or similar.
        /// </summary>
        UnusedAsset,

        /// <summary>
        /// A persistent asset with no references but marked DontDestroyOnLoad,
        /// preventing it from being unloaded.
        /// </summary>
        UnusedAssetDontUnload,

        /// <summary>
        /// A persistent asset kept alive only because a loaded AssetBundle references it. Nothing
        /// outside the bundle keeps it loaded, so unloading the bundle would free it. Distinct from
        /// <see cref="UsedAsset"/> (which is referenced from elsewhere) and <see cref="UnusedAsset"/>
        /// (which has no references at all). Detected via <see cref="RootAndImpactInfo"/> ownership:
        /// the asset's shortest-path root is an AssetBundle native object.
        /// </summary>
        UnusedAssetInAssetBundle,

        /// <summary>
        /// An AssetBundle object that still has at least one content referenced from outside the bundle.
        /// Unloading the bundle would not free that content; only its otherwise-unreferenced remainder
        /// (the <see cref="UnusedAssetInAssetBundle"/> leaves) would be freed.
        /// </summary>
        UsedAssetBundle,

        /// <summary>
        /// An AssetBundle object none of whose contents are referenced from outside the bundle. The whole
        /// bundle (and all its contents) could be unloaded. Something still references the bundle object
        /// itself (e.g. an AssetBundle manager or Addressables), so it can be unloaded deliberately.
        /// </summary>
        UnusedAssetBundle,

        /// <summary>
        /// An <see cref="UnusedAssetBundle"/> that nothing references anymore: no manager, Addressables, or
        /// user code holds the bundle object. It cannot be unloaded by name and only
        /// <c>Resources.UnloadUnusedAssets()</c> can reclaim it. Effectively a leak.
        /// </summary>
        LeakedAssetBundle,

        // =====================================================
        // Scene Objects
        // GameObjects, Transforms, Components in loaded scenes
        // =====================================================

        /// <summary>
        /// A scene object (loaded or runtime-created), not marked DontDestroyOnLoad.
        /// </summary>
        SceneObject,

        /// <summary>
        /// A scene object marked DontDestroyOnLoad.
        /// </summary>
        SceneObjectDontUnload,

        // =====================================================
        // Native Managers
        // =====================================================

        /// <summary>
        /// A native manager object representing a Unity subsystem (e.g., AudioManager).
        /// </summary>
        Manager,

        // =====================================================
        // Pure Managed Object statuses
        // (from HandlePureCSharpObjectDetails)
        // =====================================================

        /// <summary>
        /// A managed object held alive by a GCHandle because its type has
        /// [UsedByNativeCode] or [RequiredByNativeCode] attributes.
        /// </summary>
        UsedByNativeCode,

        /// <summary>
        /// A pure managed object held alive by a GCHandle (not a Unity Object type).
        /// </summary>
        GCHandleHeld,

        /// <summary>
        /// A pure managed object that has incoming references but no GCHandle.
        /// </summary>
        ManagedObjectReferenced,

        /// <summary>
        /// A managed object with no references and no GCHandle — liveness reason unknown.
        /// This indicates a bug in the crawler or snapshot processing.
        /// </summary>
        UnknownLiveness,

        // =====================================================
        // Structural / non-object node types
        // These are used by the Roots & Impact table for
        // structural nodes that represent grouping concepts
        // rather than individual objects.
        // =====================================================

        /// <summary>
        /// A managed type acting as a root due to its static fields.
        /// </summary>
        ManagedTypeRoot,

        /// <summary>
        /// A native root reference entry (subsystem allocation).
        /// </summary>
        NativeRootAllocation,

        /// <summary>
        /// A GCHandle entry (structural node in the Roots & Impact tree).
        /// </summary>
        GCHandleEntry,

        /// <summary>
        /// Filter-only status for Prefab book-keeping entries in the Roots &amp; Impact table. Never returned
        /// by <see cref="ObjectStatusComputation.GetStatus"/> (a Prefab reports its root Transform's status);
        /// it exists so the "Prefab" filter chip can select those entries via a synthesized match in
        /// RootsAndImpactModelBuilder.
        /// </summary>
        PrefabRoot,

        /// <summary>
        /// A Scene structural node.
        /// </summary>
        SceneRoot,

        /// <summary>
        /// Graphics Device/ driver subsystem allocations not directly tied to any native object.
        /// </summary>
        GraphicsBuffers,

        /// <summary>
        /// Untracked memory ranges.
        /// </summary>
        Untracked,

        /// <summary>
        /// Reserved or otherwise unused memory ranges.
        /// </summary>
        Reserved,

        /// <summary>
        /// Executables and mapped memory ranges.
        /// </summary>
        ExecutablesAndMapped,

        /// <summary>
        /// Mono or IL2CPP runtime overhead — memory required to run managed code but not
        /// attributable to any specific managed object (e.g., JIT code, GC bookkeeping, type
        /// metadata). Shown as the "Managed VM" entry in the All Of Memory table.
        /// </summary>
        VirtualMachine,

        /// <summary>
        /// Native subsystem allocations that are neither rooted to a native root reference nor
        /// referenced by anything the crawler can reach (i.e. unrooted AND unreferenced). This is
        /// memory Unity reported but that no owner could be attributed to.
        /// <also see cref="RootAndImpactInfo.UnreferencedUnrootedMemory"/>
        /// </summary>
        Unrooted,

        /// <summary>
        /// A native allocation with no native root reference that is nonetheless reached via a
        /// managed (or other) reference the crawler can follow. Unlike <see cref="Unrooted"/>, this
        /// memory IS attributed to its referencing owner in the ownership tree.
        /// <also see cref="RootAndImpactInfo.ReferencedUnrootedMemory"/>
        /// </summary>
        ReferencedUnrooted,

        /// <summary>
        /// Virtual row for an OwnershipBucket group header in the Roots & Impact table.
        /// Not a real object — used only for tree grouping.
        /// </summary>
        BucketGroup,
    }
}
