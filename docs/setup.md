# CodeCompass Setup & Installation Guide

This guide provides verified step-by-step instructions to configure, run, and validate CodeCompass locally on Windows, macOS, or Linux.

---

## 1. Prerequisites

Verify installed tools before starting:

1. **.NET 10.0 SDK** (or .NET 8.0+ / 9.0+)
   ```powershell
   dotnet --version
   ```
2. **Node.js (v18+) & npm**
   ```powershell
   node -v
   npm -v
   ```
3. **Microsoft SQL Server / LocalDB**
   - Windows includes SQL Server LocalDB (`sqllocaldb`) by default.
4. **Git CLI**
   ```powershell
   git --version
   ```

---

## 2. Database Configuration

CodeCompass uses Entity Framework Core 10 targeting SQL Server.

1. Ensure SQL Server LocalDB instance is started:
   ```powershell
   sqllocaldb start MSSQLLocalDB
   ```
2. Inspect connection string in `backend/CodeCompass.Api/appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=CodeCompassDb;Trusted_Connection=True;TrustServerCertificate=True;"
   }
   ```
3. Apply migrations to initialize the schema:
   ```powershell
   cd backend\CodeCompass.Api
   dotnet ef database update
   ```

---

## 3. IBM watsonx.ai Configuration (Optional)

IBM watsonx.ai powers live AI code generation and developer mentoring.  
*Note: If omitted, CodeCompass automatically operates in repository-grounded fallback mode without failing.*

Configure credentials via environment variables or in `appsettings.json`:

```powershell
$env:WatsonX__ApiKey="<YOUR_IBM_CLOUD_API_KEY>"
$env:WatsonX__ProjectId="<YOUR_WATSONX_PROJECT_ID>"
$env:WatsonX__Url="https://eu-de.ml.cloud.ibm.com"
$env:WatsonX__ModelId="meta-llama/llama-3-3-70b-instruct"
```

---

## 4. Running the Backend API

1. Navigate to the API directory:
   ```powershell
   cd backend\CodeCompass.Api
   ```
2. Run the API on port 5000:
   ```powershell
   dotnet run --urls "http://localhost:5000"
   ```
3. Verify backend health:
   ```powershell
   curl http://localhost:5000/api/health
   ```
   Or browse OpenAPI / Swagger at: `http://localhost:5000/swagger`

---

## 5. Running the Frontend Client

1. Open a new terminal and navigate to the frontend directory:
   ```powershell
   cd frontend
   ```
2. Install npm dependencies:
   ```powershell
   npm install
   ```
3. Start the Vite development server:
   ```powershell
   npm run dev
   ```
4. Open your browser at:
   ```
   http://localhost:5173
   ```

---

## 6. Build Verification

To execute a complete production build verification:

```powershell
# Backend (Release build)
dotnet build backend/CodeCompass.Api -c Release

# Frontend (Production bundle)
npm --prefix frontend run build
```
Both builds should complete with 0 errors and 0 warnings.
