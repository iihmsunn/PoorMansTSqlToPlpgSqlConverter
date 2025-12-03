{
  description = "tsql2psql";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-25.11";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs =
    {
      self,
      nixpkgs,
      flake-utils,
    }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = import nixpkgs {
          inherit system;
          config = {
            allowUnfree = true;
          };
        };

        dotnet-sdk = pkgs.buildEnv {
          name = "dotnet-sdk";
          paths = [
            (
              with pkgs.dotnetCorePackages;
              combinePackages [
                sdk_9_0
                sdk_8_0
              ]
            )
          ];
        };
      in
      with pkgs;
      {
        devShells.default = mkShell {
          buildInputs = [
            netcoredbg
            dotnet-sdk
            omnisharp-roslyn
          ];

          DOTNET_ROOT = "${dotnet-sdk}/share/dotnet";
        };
      }
    );
}
