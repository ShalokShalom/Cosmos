# Scale development suite

The example suite, the granular/Verify suite, the Hedgehog properties, the
differential corpus, and the test-runner entry logic. This is a development
node, not a deployable component; the `projects/scale-tests` deployable
references it (D176). The product base does not.

`Scale.Development.fsproj`; depends on the components and the kernel.
