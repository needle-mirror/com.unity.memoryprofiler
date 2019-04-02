using NUnit.Framework;

namespace Unity.MemoryProfiler.Editor.Tests
{
    [TestFixture]
    public class ReflectionUtilityTests
    {
        internal interface IMemoryProfilerPotato { }
        internal class MemoryProfilerGroundPotato : IMemoryProfilerPotato { }

        [Test]
        public void TestReflectionReturnsTypeInheritingTargetInterface()
        {
            var expectedType = typeof(MemoryProfilerGroundPotato);
            var typeList = ReflectionUtility.GetTypesImplementingInterfaceFromCurrentDomain(typeof(IMemoryProfilerPotato));
            Assert.AreEqual(1, typeList.Count); //only one type is defined
            Assert.AreEqual(expectedType, typeList[0]);
        }
    }
}