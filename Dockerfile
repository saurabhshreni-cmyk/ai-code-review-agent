FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV PORT=8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["CodeReviewAPI/CodeReviewAPI.csproj", "CodeReviewAPI/"]
RUN dotnet restore "CodeReviewAPI/CodeReviewAPI.csproj"
COPY CodeReviewAPI/ CodeReviewAPI/
RUN dotnet build "CodeReviewAPI/CodeReviewAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CodeReviewAPI/CodeReviewAPI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CodeReviewAPI.dll"]
