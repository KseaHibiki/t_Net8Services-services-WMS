pipeline {
    agent any

    environment {
        REGISTRY = 'localhost:5005'
        IMAGE_NAME = 'wms-api'
        IMAGE_TAG = "${env.BUILD_NUMBER}"
    }

    stages {
        stage('Checkout') {
            steps {
                dir('t_Net8Services') {
                    checkout([
                        $class: 'GitSCM',
                        branches: [[name: '*/main']],
                        userRemoteConfigs: [[
                            url: 'https://github.com/KseaHibiki/t_Net8Services-services-WMS.git',
                            credentialsId: 'github-pat-token'
                        ]],
                        extensions: [[$class: 'RelativeTargetDirectory', relativeTargetDir: 'services/WMS/src']]
                    ])
                    checkout([
                        $class: 'GitSCM',
                        branches: [[name: '*/main']],
                        userRemoteConfigs: [[
                            url: 'https://github.com/KseaHibiki/t_Net8Services-shared-Shop.Events.git',
                            credentialsId: 'github-pat-token'
                        ]],
                        extensions: [[$class: 'RelativeTargetDirectory', relativeTargetDir: 'shared/Shop.Events']]
                    ])
                }
            }
        }

        stage('Restore & Build') {
            steps {
                dir('t_Net8Services') {
                    bat 'dotnet restore services/WMS/src/WMS.API/WMS.API.csproj'
                    bat 'dotnet build services/WMS/src/WMS.API/WMS.API.csproj -c Release --no-restore'
                }
            }
        }

        stage('Docker Build & Push') {
            steps {
                dir('t_Net8Services') {
                    bat "docker build -f services/WMS/src/WMS.API/Dockerfile -t ${REGISTRY}/${IMAGE_NAME}:%IMAGE_TAG% -t ${REGISTRY}/${IMAGE_NAME}:latest ."
                    bat "docker push ${REGISTRY}/${IMAGE_NAME}:%IMAGE_TAG%"
                    bat "docker push ${REGISTRY}/${IMAGE_NAME}:latest"
                }
            }
        }
    }

    post {
        success {
            echo "✅ WMS API 镜像已推送: ${REGISTRY}/${IMAGE_NAME}:${IMAGE_TAG}"
        }
        failure {
            echo '❌ WMS API 构建失败'
        }
        always {
            cleanWs()
        }
    }
}