// Newer MSTest versions require the parallelization of tests to be decided explicitly. These tests share no state.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
