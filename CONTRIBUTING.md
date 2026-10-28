# Contributing to Fetchshelf

Thanks for wanting to help. Bug reports, ideas, documentation fixes, code and reviews are all welcome.

The full [contributors guide](docs/contributors-guide.md) covers setting up a development machine (apt and
dnf distributions), building and testing, and the fork, branch, commit and pull request workflow, with `gh`
examples throughout.

## In short

1. **Talk first for anything bigger than a small fix.** Open an issue or a discussion with your plan, so no
   one builds something that doesn't fit.
2. **Fork, then branch** from an up-to-date `main`: `gh repo fork crimsonking60/fetchshelf --clone`, then
   `git switch -c fix/123-short-description`.
3. **Pass all three checks** before you push:

   ```sh
   dotnet build && dotnet test && dotnet format --verify-no-changes
   ```

   The build treats every warning as an error, and new behavior and bug fixes need tests.
4. **Write commit messages** with an imperative subject line and a body that explains why.
5. **Open a pull request** against `main`: `gh pr create --repo crimsonking60/fetchshelf --base main --fill`.

## Reporting bugs

Search the [existing issues](https://github.com/crimsonking60/fetchshelf/issues) first. Include the version or
commit, what you did, what you expected and what happened, the relevant log lines, and the relevant part of
your configuration. See [Issues](docs/contributors-guide.md#issues) for details.

**Remove secrets first.** Never post your `mam_id`, qBittorrent password, or Audiobookshelf or Notion tokens.

## Security problems

Don't report them in public issues or discussions. Use the repository's **Security** tab and choose
**Report a vulnerability**.

## License

By contributing, you agree that your contribution is released under the project's [MIT License](LICENSE).
