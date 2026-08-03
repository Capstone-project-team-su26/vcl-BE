# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files first to leverage Docker layer caching on restore
COPY VCL_API/VCL_API.csproj VCL_API/
COPY VCL_BLL/VCL_BLL.csproj VCL_BLL/
COPY VCL_DLL/VCL_DLL.csproj VCL_DLL/
RUN dotnet restore VCL_API/VCL_API.csproj

# Copy the rest of the source and publish
COPY . .
RUN dotnet publish VCL_API/VCL_API.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# QuestPDF cần font hệ thống để render phiếu PDF; ảnh aspnet không kèm font nào,
# thiếu font thì chữ tiếng Việt ra ô vuông.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-liberation fontconfig \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Listen on 8080 inside the container (no HTTPS cert needed)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VCL_API.dll"]
