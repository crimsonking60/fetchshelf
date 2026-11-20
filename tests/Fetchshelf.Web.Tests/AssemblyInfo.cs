// Test classes run in parallel: each FetchshelfWebAppFixture hands its host its own paths as arguments, not through the
// process environment. The classes that need the whole process to themselves are in the ProcessWide collection, which
// runs alone after the others.
